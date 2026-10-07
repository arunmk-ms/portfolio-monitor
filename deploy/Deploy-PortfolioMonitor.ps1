#Requires -Version 7.0
<#
.SYNOPSIS
    Provisions Azure resources for portfolio-monitor and deploys both apps from this machine.

.DESCRIPTION
    A single local-first deployment path: no build pipeline, no service principal, no secrets in
    source control. Every step is idempotent, so re-running the script after a failure (or after a
    code change) picks up where it left off rather than duplicating resources.

    Resources created (steady-state cost is roughly USD 6-10/month):

        Storage account                required by the Functions runtime    ~$1
        Log Analytics + App Insights   telemetry, 5 GB/month free tier      ~$0
        Azure SQL Database (Basic)     PortfolioDb, 5 DTU / 2 GB            ~$5
        Function App (Flex Consumption) portfolioapp timer trigger          ~$0-2
        Static Web App (Free)          portfolioweb SPA                     $0
        Consumption budget             alert-only guard rail                $0

    The SQL server is created with Entra-only authentication, so no SQL password is ever generated,
    stored, or transmitted. The Function App reaches the database through its system-assigned
    managed identity; the deploying user is the Entra admin and applies migrations over an access
    token obtained from the Azure CLI.

.PARAMETER SubscriptionId
    Target subscription. Use the personal subscription, not a corporate one.

.PARAMETER AlphaVantageApiKey
    Alpha Vantage API key. Prompted for securely when omitted and not already set on the app.

.EXAMPLE
    ./Deploy-PortfolioMonitor.ps1 -SubscriptionId '00000000-0000-0000-0000-000000000000'

.EXAMPLE
    # Redeploy application code only, leaving infrastructure and the database untouched.
    ./Deploy-PortfolioMonitor.ps1 -SubscriptionId '...' -SkipInfrastructure -SkipDatabase
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$SubscriptionId,

    [string]$ResourceGroup = 'rg-portfolio-monitor',

    # Must be a Flex Consumption region; the script validates this and lists alternatives.
    [string]$Location = 'australiaeast',

    # Static Web Apps exists in only a handful of regions — far fewer than Functions or SQL. Do not
    # widen this list to match -Location: Azure rejects anything outside it. Leave the value unset
    # and the script picks the closest supported region for you.
    [ValidateSet('centralus', 'eastus2', 'westus2', 'westeurope', 'eastasia')]
    [string]$StaticWebAppLocation,

    [ValidatePattern('^[a-z][a-z0-9]{2,11}$')]
    [string]$NamePrefix = 'portfolio',

    [string]$AlphaVantageApiKey,

    [string]$Symbols = 'MSFT,NVDA',

    # NCRONTAB, evaluated in UTC. 13:00-21:00 UTC covers the US session under EDT; the app's
    # IMarketCalendar makes the precise open/close/holiday decision, so this is only a coarse gate.
    [string]$QuoteMonitorSchedule = '0 */15 13-21 * * 1-5',

    [ValidateRange(1, 10000)]
    [int]$MonthlyBudget = 30,

    [string]$BudgetAlertEmail,

    # Public IP allowed through the SQL firewall so migrations can run from here. Auto-detected.
    [string]$ClientIpAddress,

    [switch]$SkipInfrastructure,
    [switch]$SkipDatabase,
    [switch]$SkipFunctionApp,
    [switch]$SkipWeb,
    [switch]$SkipBudget
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$functionProject = Join-Path $repoRoot 'portfolioapp'
$webProject = Join-Path $repoRoot 'portfolioweb'

# ---------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------

$script:stepNumber = 0

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    $script:stepNumber++
    Write-Host ''
    Write-Host ("==> [{0}] {1}" -f $script:stepNumber, $Message) -ForegroundColor Cyan
}

function Write-Detail {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "    $Message" -ForegroundColor DarkGray
}

function Write-Skipped {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "    $Message" -ForegroundColor Yellow
}

<#
    The Azure CLI writes progress, deprecation, and (on some machines) Python warnings to stderr
    while still succeeding, so stderr cannot be treated as failure. Capture it separately and
    surface it only when the exit code is non-zero.
#>
function Invoke-Az {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Arguments,
        [switch]$AsJson,
        [switch]$IgnoreErrors
    )

    $stderrPath = [System.IO.Path]::GetTempFileName()
    try {
        $stdout = & az @Arguments 2>$stderrPath
        $exitCode = $LASTEXITCODE

        if ($exitCode -ne 0) {
            if ($IgnoreErrors) { return $null }
            $stderr = (Get-Content -Raw -LiteralPath $stderrPath -ErrorAction SilentlyContinue)
            throw "az $($Arguments -join ' ') failed with exit code $exitCode.`n$stderr"
        }

        $text = ($stdout | Out-String).Trim()
        if (-not $AsJson) { return $text }
        if ([string]::IsNullOrWhiteSpace($text)) { return $null }
        return $text | ConvertFrom-Json
    }
    finally {
        Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function Test-AzResourceExists {
    param([Parameter(Mandatory)][string[]]$ShowArguments)
    return $null -ne (Invoke-Az -IgnoreErrors -AsJson -Arguments ($ShowArguments + @('--output', 'json')))
}

function Assert-Tool {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$InstallHint
    )
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required tool '$Name' was not found on PATH. $InstallHint"
    }
}

<#
    Executes T-SQL against Azure SQL using an Entra access token from the Azure CLI. This avoids
    DefaultAzureCredential, which on a machine signed in to several tenants can silently pick the
    wrong identity, and keeps the whole script on one authentication path.
#>
function Invoke-SqlBatch {
    param(
        [Parameter(Mandatory)][string]$ServerFqdn,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$AccessToken,
        [Parameter(Mandatory)][string[]]$Batches
    )

    $connection = [System.Data.SqlClient.SqlConnection]::new()
    $connection.ConnectionString =
        "Server=tcp:$ServerFqdn,1433;Initial Catalog=$Database;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;"
    $connection.AccessToken = $AccessToken

    try {
        $connection.Open()
        foreach ($batch in $Batches) {
            if ([string]::IsNullOrWhiteSpace($batch)) { continue }
            $command = $connection.CreateCommand()
            $command.CommandText = $batch
            $command.CommandTimeout = 180
            [void]$command.ExecuteNonQuery()
        }
    }
    finally {
        $connection.Dispose()
    }
}

# `dotnet ef migrations script` emits GO separators, which are a sqlcmd construct rather than
# T-SQL, so the driver rejects them. Split the script into the batches GO delimits. The trailing
# \r? matters: the generated file is CRLF, and in .NET multiline mode `$` anchors before the \n
# only, so without it the pattern never matches a single line.
function Split-SqlBatches {
    param([Parameter(Mandatory)][string]$Script)
    return [regex]::Split($Script, '(?im)^[\t ]*GO[\t ]*(?:--.*?)?\r?$')
}

function Get-PublicIpAddress {
    foreach ($endpoint in @('https://api.ipify.org', 'https://ifconfig.me/ip')) {
        try {
            $ip = (Invoke-RestMethod -Uri $endpoint -TimeoutSec 10 -ErrorAction Stop).ToString().Trim()
            if ($ip -match '^\d{1,3}(\.\d{1,3}){3}$') { return $ip }
        }
        catch {
            Write-Detail "Could not reach $endpoint to detect the public IP."
        }
    }
    throw 'Unable to detect this machine''s public IP address. Pass -ClientIpAddress explicitly.'
}

<#
    Creates a SQL firewall rule, or updates it when one of that name already exists.

    This matters on a re-run from a different network or after a DHCP lease change: `create` fails
    on a duplicate rule name, and simply ignoring that error would leave the stale address in place.
    The failure would then resurface much later as an opaque login error when migrations run, with
    nothing pointing back at the firewall.
#>
function Set-SqlFirewallRule {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$StartIp,
        [Parameter(Mandatory)][string]$EndIp
    )

    # Invoke-Az returns an empty string on success with --output none, and $null when it failed and
    # errors were ignored, so this distinguishes the two.
    $created = Invoke-Az -IgnoreErrors -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--resource-group', $ResourceGroup, '--server', $sqlServerName,
        '--name', $Name,
        '--start-ip-address', $StartIp, '--end-ip-address', $EndIp, '--output', 'none')

    if ($null -ne $created) { return }

    [void](Invoke-Az -Arguments @(
            'sql', 'server', 'firewall-rule', 'update',
            '--resource-group', $ResourceGroup, '--server', $sqlServerName,
            '--name', $Name,
            '--start-ip-address', $StartIp, '--end-ip-address', $EndIp, '--output', 'none'))
}

# ---------------------------------------------------------------------------------------------
# Deterministic resource names
# ---------------------------------------------------------------------------------------------

# Derived from the subscription, resource group, and prefix so repeated runs always target the same
# resources without needing a state file, while still producing globally unique names.
$nameSeed = [System.Text.Encoding]::UTF8.GetBytes("$SubscriptionId|$ResourceGroup|$NamePrefix".ToLowerInvariant())
$nameHash = [System.Security.Cryptography.SHA256]::HashData($nameSeed)
$suffix = ([System.BitConverter]::ToString($nameHash) -replace '-', '').Substring(0, 6).ToLowerInvariant()

$storageName = "st$NamePrefix$suffix"
if ($storageName.Length -gt 24) { $storageName = $storageName.Substring(0, 24) }
$logAnalyticsName = "log-$NamePrefix-$suffix"
$appInsightsName = "appi-$NamePrefix-$suffix"
$sqlServerName = "sql-$NamePrefix-$suffix"
$sqlDatabaseName = 'portfoliomonitor'
$functionAppName = "func-$NamePrefix-$suffix"
$staticWebAppName = "stapp-$NamePrefix-$suffix"
$budgetName = "budget-$NamePrefix-monthly"
$sqlServerFqdn = "$sqlServerName.database.windows.net"

# ---------------------------------------------------------------------------------------------
# Preflight
# ---------------------------------------------------------------------------------------------

Write-Step 'Checking local tooling'

Assert-Tool -Name 'az' -InstallHint 'Install the Azure CLI: https://aka.ms/installazurecli'
Assert-Tool -Name 'dotnet' -InstallHint 'Install the .NET 8 SDK: https://dotnet.microsoft.com/download'
if (-not $SkipFunctionApp) {
    Assert-Tool -Name 'func' -InstallHint 'npm install -g azure-functions-core-tools@4 --unsafe-perm true'
}
if (-not $SkipWeb) {
    Assert-Tool -Name 'npm' -InstallHint 'Install Node.js LTS: https://nodejs.org'
    Assert-Tool -Name 'npx' -InstallHint 'Install Node.js LTS: https://nodejs.org'
}
Write-Detail 'All required tools are present.'

Write-Step 'Selecting the Azure subscription'

$account = Invoke-Az -IgnoreErrors -AsJson -Arguments @('account', 'show', '--output', 'json')
if (-not $account) {
    Write-Detail 'Not signed in. Launching az login...'
    [void](Invoke-Az -Arguments @('login', '--output', 'none'))
}

[void](Invoke-Az -Arguments @('account', 'set', '--subscription', $SubscriptionId))
$account = Invoke-Az -AsJson -Arguments @('account', 'show', '--output', 'json')

$signedInUser = $account.user.name
Write-Detail "Subscription : $($account.name) ($($account.id))"
Write-Detail "Tenant       : $($account.tenantId)"
Write-Detail "Signed in as : $signedInUser"

if (-not $BudgetAlertEmail) { $BudgetAlertEmail = $signedInUser }

# Static Web Apps is unavailable in most regions, including australiaeast, so it cannot simply
# follow -Location. Map to the nearest supported one. This only affects where the app's metadata
# lives: static content is served from Azure's global edge network regardless, so a "distant"
# region here costs nothing in page-load latency.
if (-not $StaticWebAppLocation) {
    $StaticWebAppLocation = switch -Regex ($Location.ToLowerInvariant()) {
        '^(australia|.*asia|japan|korea|.*india|china|uae|qatar|israel)' { 'eastasia'; break }
        '^(.*europe|uk|france|germany|switzerland|norway|sweden|poland|italy|spain|southafrica)' { 'westeurope'; break }
        '^west(us|centralus)' { 'westus2'; break }
        '^(central|northcentral|southcentral)us' { 'centralus'; break }
        default { 'eastus2' }
    }
    Write-Detail "Static Web Apps is not offered in every region; using '$StaticWebAppLocation' as the closest supported one."
}

# The object ID is needed to make the signed-in user the Entra admin of the SQL server.
$signedInUserObjectId = Invoke-Az -IgnoreErrors -Arguments @('ad', 'signed-in-user', 'show', '--query', 'id', '--output', 'tsv')
if ([string]::IsNullOrWhiteSpace($signedInUserObjectId)) {
    throw "Could not read the signed-in user's object ID from Microsoft Graph. Run 'az login --tenant $($account.tenantId)' and try again."
}

Write-Host ''
Write-Host 'Resources this run will create or update:' -ForegroundColor Green
Write-Detail "Resource group  : $ResourceGroup ($Location)"
Write-Detail "Storage         : $storageName"
Write-Detail "Log Analytics   : $logAnalyticsName"
Write-Detail "App Insights    : $appInsightsName"
Write-Detail "SQL server      : $sqlServerName / $sqlDatabaseName"
Write-Detail "Function App    : $functionAppName"
Write-Detail "Static Web App  : $staticWebAppName ($StaticWebAppLocation)"

# ---------------------------------------------------------------------------------------------
# Infrastructure
# ---------------------------------------------------------------------------------------------

$appInsightsConnectionString = $null
$staticWebAppHostname = $null

if ($SkipInfrastructure) {
    Write-Step 'Provisioning infrastructure'
    Write-Skipped 'Skipped (-SkipInfrastructure).'
}
else {
    Write-Step 'Registering resource providers'

    foreach ($provider in @('Microsoft.Web', 'Microsoft.Storage', 'Microsoft.Sql', 'Microsoft.Insights', 'Microsoft.OperationalInsights')) {
        $state = Invoke-Az -Arguments @('provider', 'show', '--namespace', $provider, '--query', 'registrationState', '--output', 'tsv')
        if ($state -ne 'Registered') {
            Write-Detail "Registering $provider (this can take a minute)..."
            [void](Invoke-Az -Arguments @('provider', 'register', '--namespace', $provider, '--wait'))
        }
    }
    Write-Detail 'All required providers are registered.'

    Write-Step 'Ensuring the Application Insights CLI extension is installed'
    [void](Invoke-Az -IgnoreErrors -Arguments @('extension', 'add', '--name', 'application-insights', '--only-show-errors', '--output', 'none'))
    Write-Detail 'Extension ready.'

    Write-Step "Validating '$Location' supports the Flex Consumption plan"

    $flexRegions = @((Invoke-Az -AsJson -Arguments @('functionapp', 'list-flexconsumption-locations', '--output', 'json')) |
        ForEach-Object { $_.name.ToLowerInvariant() })
    if ($flexRegions -notcontains $Location.ToLowerInvariant()) {
        throw "Region '$Location' does not offer Flex Consumption. Choose one of: $($flexRegions -join ', ')"
    }
    Write-Detail "$Location supports Flex Consumption."

    Write-Step "Creating resource group '$ResourceGroup'"
    [void](Invoke-Az -Arguments @('group', 'create', '--name', $ResourceGroup, '--location', $Location, '--output', 'none'))
    Write-Detail 'Resource group ready.'

    Write-Step "Creating storage account '$storageName'"
    if (Test-AzResourceExists @('storage', 'account', 'show', '--name', $storageName, '--resource-group', $ResourceGroup)) {
        Write-Detail 'Storage account already exists.'
    }
    else {
        [void](Invoke-Az -Arguments @(
                'storage', 'account', 'create',
                '--name', $storageName,
                '--resource-group', $ResourceGroup,
                '--location', $Location,
                '--sku', 'Standard_LRS',
                '--kind', 'StorageV2',
                '--min-tls-version', 'TLS1_2',
                '--allow-blob-public-access', 'false',
                '--output', 'none'))
        Write-Detail 'Storage account created.'
    }

    Write-Step "Creating Log Analytics workspace '$logAnalyticsName'"
    $workspace = Invoke-Az -IgnoreErrors -AsJson -Arguments @(
        'monitor', 'log-analytics', 'workspace', 'show',
        '--resource-group', $ResourceGroup, '--workspace-name', $logAnalyticsName, '--output', 'json')
    if (-not $workspace) {
        $workspace = Invoke-Az -AsJson -Arguments @(
            'monitor', 'log-analytics', 'workspace', 'create',
            '--resource-group', $ResourceGroup,
            '--workspace-name', $logAnalyticsName,
            '--location', $Location,
            '--retention-time', '30',
            '--output', 'json')
        Write-Detail 'Workspace created.'
    }
    else {
        Write-Detail 'Workspace already exists.'
    }

    Write-Step "Creating Application Insights '$appInsightsName'"
    $appInsights = Invoke-Az -IgnoreErrors -AsJson -Arguments @(
        'monitor', 'app-insights', 'component', 'show',
        '--app', $appInsightsName, '--resource-group', $ResourceGroup, '--output', 'json')
    if (-not $appInsights) {
        $appInsights = Invoke-Az -AsJson -Arguments @(
            'monitor', 'app-insights', 'component', 'create',
            '--app', $appInsightsName,
            '--resource-group', $ResourceGroup,
            '--location', $Location,
            '--workspace', $workspace.id,
            '--application-type', 'web',
            '--output', 'json')
        Write-Detail 'Application Insights created.'
    }
    else {
        Write-Detail 'Application Insights already exists.'
    }
    $appInsightsConnectionString = $appInsights.connectionString

    Write-Step "Creating SQL server '$sqlServerName' with Entra-only authentication"
    if (Test-AzResourceExists @('sql', 'server', 'show', '--name', $sqlServerName, '--resource-group', $ResourceGroup)) {
        Write-Detail 'SQL server already exists.'
    }
    else {
        # --enable-ad-only-auth means no SQL login or password is ever created for this server.
        [void](Invoke-Az -Arguments @(
                'sql', 'server', 'create',
                '--name', $sqlServerName,
                '--resource-group', $ResourceGroup,
                '--location', $Location,
                '--enable-ad-only-auth',
                '--external-admin-principal-type', 'User',
                '--external-admin-name', $signedInUser,
                '--external-admin-sid', $signedInUserObjectId,
                '--minimal-tls-version', '1.2',
                '--output', 'none'))
        Write-Detail "SQL server created; $signedInUser is the Entra admin."
    }

    Write-Step 'Configuring the SQL firewall'

    # Start and end 0.0.0.0 is the documented sentinel for "allow other Azure services", which is
    # how the Function App reaches the server without a virtual network.
    Set-SqlFirewallRule -Name 'AllowAzureServices' -StartIp '0.0.0.0' -EndIp '0.0.0.0'

    if (-not $ClientIpAddress) { $ClientIpAddress = Get-PublicIpAddress }
    Set-SqlFirewallRule -Name 'DeploymentClient' -StartIp $ClientIpAddress -EndIp $ClientIpAddress
    Write-Detail "Allowed Azure services and this machine ($ClientIpAddress)."

    Write-Step "Creating SQL database '$sqlDatabaseName' (Basic, 5 DTU / 2 GB)"
    if (Test-AzResourceExists @('sql', 'db', 'show', '--name', $sqlDatabaseName, '--server', $sqlServerName, '--resource-group', $ResourceGroup)) {
        Write-Detail 'Database already exists.'
    }
    else {
        [void](Invoke-Az -Arguments @(
                'sql', 'db', 'create',
                '--name', $sqlDatabaseName,
                '--server', $sqlServerName,
                '--resource-group', $ResourceGroup,
                '--service-objective', 'Basic',
                '--backup-storage-redundancy', 'Local',
                '--output', 'none'))
        Write-Detail 'Database created.'
    }

    Write-Step "Creating Function App '$functionAppName' (Flex Consumption, .NET 8 isolated)"
    if (Test-AzResourceExists @('functionapp', 'show', '--name', $functionAppName, '--resource-group', $ResourceGroup)) {
        Write-Detail 'Function App already exists.'
    }
    else {
        [void](Invoke-Az -Arguments @(
                'functionapp', 'create',
                '--name', $functionAppName,
                '--resource-group', $ResourceGroup,
                '--storage-account', $storageName,
                '--flexconsumption-location', $Location,
                '--runtime', 'dotnet-isolated',
                '--runtime-version', '8.0',
                '--instance-memory', '2048',
                '--output', 'none'))
        Write-Detail 'Function App created.'
    }

    Write-Step 'Assigning the Function App a system-assigned managed identity'
    $identity = Invoke-Az -AsJson -Arguments @(
        'functionapp', 'identity', 'assign',
        '--name', $functionAppName, '--resource-group', $ResourceGroup, '--output', 'json')
    Write-Detail "Principal ID: $($identity.principalId)"

    Write-Step 'Applying Function App settings'

    if (-not $AlphaVantageApiKey) {
        $existingKey = Invoke-Az -IgnoreErrors -Arguments @(
            'functionapp', 'config', 'appsettings', 'list',
            '--name', $functionAppName, '--resource-group', $ResourceGroup,
            '--query', "[?name=='AlphaVantage__ApiKey'].value | [0]", '--output', 'tsv')

        if ([string]::IsNullOrWhiteSpace($existingKey)) {
            $secureKey = Read-Host -Prompt 'Alpha Vantage API key' -AsSecureString
            $AlphaVantageApiKey = [System.Net.NetworkCredential]::new('', $secureKey).Password
            if ([string]::IsNullOrWhiteSpace($AlphaVantageApiKey)) {
                throw 'An Alpha Vantage API key is required. Get a free key at https://www.alphavantage.co/support/#api-key'
            }
        }
        else {
            Write-Detail 'Reusing the Alpha Vantage key already stored on the app.'
        }
    }

    # Authentication=Active Directory Managed Identity makes Microsoft.Data.SqlClient fetch a token
    # for the app's system-assigned identity, so the connection string holds no secret at all.
    $dbConnectionString = "Server=tcp:$sqlServerFqdn,1433;Initial Catalog=$sqlDatabaseName;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;"

    $settings = @(
        "QuoteMonitorSchedule=$QuoteMonitorSchedule",
        'MarketHours__Enabled=true',
        'MarketHours__TimeZone=America/New_York',
        "AlphaVantage__Symbols=$Symbols",
        "ConnectionStrings__PortfolioDb=$dbConnectionString",
        "APPLICATIONINSIGHTS_CONNECTION_STRING=$appInsightsConnectionString",
        "OTEL_SERVICE_NAME=$functionAppName"
    )
    if ($AlphaVantageApiKey) {
        $settings += "AlphaVantage__ApiKey=$AlphaVantageApiKey"
    }

    [void](Invoke-Az -Arguments (@(
                'functionapp', 'config', 'appsettings', 'set',
                '--name', $functionAppName, '--resource-group', $ResourceGroup,
                '--settings') + $settings + @('--output', 'none')))
    Write-Detail "$($settings.Count) application settings applied."

    # The Static Web App is created here rather than alongside the front-end build because its
    # hostname is the CORS origin the Function App has to allow, and that must be in place before
    # the browser ever calls the API.
    Write-Step "Creating Static Web App '$staticWebAppName' (Free tier)"
    $staticWebApp = Invoke-Az -IgnoreErrors -AsJson -Arguments @(
        'staticwebapp', 'show', '--name', $staticWebAppName, '--resource-group', $ResourceGroup, '--output', 'json')
    if (-not $staticWebApp) {
        $staticWebApp = Invoke-Az -AsJson -Arguments @(
            'staticwebapp', 'create',
            '--name', $staticWebAppName,
            '--resource-group', $ResourceGroup,
            '--location', $StaticWebAppLocation,
            '--sku', 'Free',
            '--output', 'json')
        Write-Detail 'Static Web App created.'
    }
    else {
        Write-Detail 'Static Web App already exists.'
    }
    $staticWebAppHostname = $staticWebApp.defaultHostname

    Write-Step 'Allowing the Static Web App origin through Function App CORS'

    # The SPA is served from a different host than the API, so every call is cross-origin, and the
    # x-functions-key header makes each one preflight. Without this the browser blocks the upload
    # before the request is ever sent.
    $webOrigin = "https://$staticWebAppHostname"
    $existingCors = Invoke-Az -IgnoreErrors -AsJson -Arguments @(
        'functionapp', 'cors', 'show', '--name', $functionAppName, '--resource-group', $ResourceGroup, '--output', 'json')
    $allowedOrigins = @()
    if ($existingCors -and $existingCors.PSObject.Properties.Name -contains 'allowedOrigins' -and $existingCors.allowedOrigins) {
        $allowedOrigins = @($existingCors.allowedOrigins)
    }

    if ($allowedOrigins -contains $webOrigin) {
        Write-Detail "$webOrigin is already allowed."
    }
    else {
        [void](Invoke-Az -Arguments @(
                'functionapp', 'cors', 'add',
                '--name', $functionAppName, '--resource-group', $ResourceGroup,
                '--allowed-origins', $webOrigin, '--output', 'none'))
        Write-Detail "Allowed $webOrigin."
    }
}

# ---------------------------------------------------------------------------------------------
# Database schema and managed identity grant
# ---------------------------------------------------------------------------------------------

if ($SkipDatabase) {
    Write-Step 'Applying database migrations'
    Write-Skipped 'Skipped (-SkipDatabase).'
}
else {
    Write-Step 'Requesting a SQL access token'
    $sqlAccessToken = Invoke-Az -Arguments @(
        'account', 'get-access-token', '--resource', 'https://database.windows.net/', '--query', 'accessToken', '--output', 'tsv')
    if ([string]::IsNullOrWhiteSpace($sqlAccessToken)) { throw 'Failed to obtain an Azure SQL access token.' }
    Write-Detail 'Token acquired.'

    Write-Step 'Generating and applying EF Core migrations'

    $migrationScript = Join-Path ([System.IO.Path]::GetTempPath()) "portfolio-migrations-$suffix.sql"
    try {
        # --idempotent guards every migration with a check against __EFMigrationsHistory, so
        # applying the script repeatedly is safe and generating it needs no database connection.
        & dotnet ef migrations script `
            --idempotent `
            --project $functionProject `
            --startup-project $functionProject `
            --output $migrationScript
        if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations script failed with exit code $LASTEXITCODE." }

        $batches = Split-SqlBatches -Script (Get-Content -Raw -LiteralPath $migrationScript)
        Invoke-SqlBatch -ServerFqdn $sqlServerFqdn -Database $sqlDatabaseName -AccessToken $sqlAccessToken -Batches $batches
        Write-Detail "Schema applied to $sqlDatabaseName."
    }
    finally {
        Remove-Item -LiteralPath $migrationScript -Force -ErrorAction SilentlyContinue
    }

    Write-Step "Granting '$functionAppName' read/write access to the database"

    # The contained database user is named after the app, which is how Azure SQL resolves a
    # system-assigned identity through FROM EXTERNAL PROVIDER.
    #
    # The statement is built into a variable before execution rather than concatenated inside
    # EXEC(...). T-SQL only allows literals and variables to be concatenated in that position, so a
    # QUOTENAME() call there is a syntax error. Assigning with SET evaluates it first, and
    # sp_executesql then receives a finished string. QUOTENAME still does the escaping that keeps
    # the dynamic SQL safe.
    $grantSql = @"
DECLARE @principal sysname = N'$($functionAppName.Replace("'", "''"))';
DECLARE @sql nvarchar(max);

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @principal)
BEGIN
    SET @sql = N'CREATE USER ' + QUOTENAME(@principal) + N' FROM EXTERNAL PROVIDER;';
    EXEC sp_executesql @sql;
END

SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@principal) + N';';
EXEC sp_executesql @sql;

SET @sql = N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@principal) + N';';
EXEC sp_executesql @sql;
"@

    Invoke-SqlBatch -ServerFqdn $sqlServerFqdn -Database $sqlDatabaseName -AccessToken $sqlAccessToken -Batches @($grantSql)
    Write-Detail 'Managed identity granted db_datareader and db_datawriter.'
}

# ---------------------------------------------------------------------------------------------
# portfolioapp
# ---------------------------------------------------------------------------------------------

if ($SkipFunctionApp) {
    Write-Step 'Publishing portfolioapp'
    Write-Skipped 'Skipped (-SkipFunctionApp).'
}
else {
    Write-Step "Publishing portfolioapp to '$functionAppName'"

    Push-Location $functionProject
    try {
        # The Functions Worker SDK generates an internal WorkerExtensions.csproj under obj/ on every
        # build, including `dotnet test`. Core Tools scans recursively for the project to publish and
        # aborts with "Expected 1 .csproj or .fsproj but found 2" when it sees more than one. Clear
        # the generated copies first; the build that publish runs recreates them.
        $generatedProjects = @(
            Get-ChildItem -Path (Join-Path $functionProject 'obj') -Recurse -Directory -Filter 'WorkerExtensions' -ErrorAction SilentlyContinue)
        foreach ($generated in $generatedProjects) {
            Remove-Item -LiteralPath $generated.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($generatedProjects.Count -gt 0) {
            Write-Detail "Cleared $($generatedProjects.Count) generated WorkerExtensions project(s) from obj/."
        }

        $projectCount = @(Get-ChildItem -Path $functionProject -Recurse -Filter '*.csproj' -ErrorAction SilentlyContinue).Count
        if ($projectCount -ne 1) {
            throw "Expected exactly one .csproj under '$functionProject' but found $projectCount. Core Tools cannot pick a project to publish; remove the extra project files and retry."
        }

        # No runtime flag needed: publish infers dotnet-isolated from the project file, and
        # local.settings.json is excluded by CopyToPublishDirectory=Never so no secret is uploaded.
        & func azure functionapp publish $functionAppName
        if ($LASTEXITCODE -ne 0) { throw "func azure functionapp publish failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }
    Write-Detail 'Function App deployed.'
}

# ---------------------------------------------------------------------------------------------
# portfolioweb
# ---------------------------------------------------------------------------------------------

if ($SkipWeb) {
    Write-Step 'Deploying portfolioweb'
    Write-Skipped 'Skipped (-SkipWeb).'
}
else {
    Write-Step 'Resolving the Static Web App and API credentials'

    # -SkipInfrastructure leaves the app already created, so read it back rather than assuming the
    # hostname captured earlier in this run is populated.
    if (-not $staticWebAppHostname) {
        $existingWebApp = Invoke-Az -IgnoreErrors -AsJson -Arguments @(
            'staticwebapp', 'show', '--name', $staticWebAppName, '--resource-group', $ResourceGroup, '--output', 'json')
        if (-not $existingWebApp) {
            throw "Static Web App '$staticWebAppName' does not exist yet. Run once without -SkipInfrastructure first."
        }
        $staticWebAppHostname = $existingWebApp.defaultHostname
    }

    # PortfolioUploadFunction is AuthorizationLevel.Function, so the SPA has to present a key. The
    # default host key covers every function-level route on the app.
    $functionKey = Invoke-Az -Arguments @(
        'functionapp', 'keys', 'list',
        '--name', $functionAppName, '--resource-group', $ResourceGroup,
        '--query', 'functionKeys.default', '--output', 'tsv')
    if ([string]::IsNullOrWhiteSpace($functionKey)) {
        throw "Could not read the default function key for '$functionAppName'."
    }

    $apiBaseUrl = "https://$functionAppName.azurewebsites.net/api"
    Write-Detail "Web origin   : https://$staticWebAppHostname"
    Write-Detail "API base URL : $apiBaseUrl"
    Write-Detail 'Function key : retrieved.'

    Write-Step 'Building portfolioweb'
    Push-Location $webProject
    try {
        if (-not (Test-Path (Join-Path $webProject 'node_modules'))) {
            & npm ci
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
        }

        # Vite inlines import.meta.env at build time, so these have to be real environment
        # variables during the build. They are removed afterwards so the key does not linger in
        # the caller's session.
        $env:VITE_API_BASE_URL = $apiBaseUrl
        $env:VITE_API_FUNCTION_KEY = $functionKey
        try {
            & npm run build
            if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE." }
        }
        finally {
            Remove-Item Env:\VITE_API_BASE_URL -ErrorAction SilentlyContinue
            Remove-Item Env:\VITE_API_FUNCTION_KEY -ErrorAction SilentlyContinue
        }
        Write-Detail 'Production bundle built into portfolioweb/dist.'

        Write-Step 'Uploading portfolioweb'

        $deploymentToken = Invoke-Az -Arguments @(
            'staticwebapp', 'secrets', 'list',
            '--name', $staticWebAppName, '--resource-group', $ResourceGroup,
            '--query', 'properties.apiKey', '--output', 'tsv')
        if ([string]::IsNullOrWhiteSpace($deploymentToken)) { throw 'Could not read the Static Web App deployment token.' }

        & npx --yes @azure/static-web-apps-cli deploy './dist' --deployment-token $deploymentToken --env production
        if ($LASTEXITCODE -ne 0) { throw "Static Web Apps CLI deploy failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }
    Write-Detail 'Static Web App deployed.'
}

# ---------------------------------------------------------------------------------------------
# Budget guard rail
# ---------------------------------------------------------------------------------------------

if ($SkipBudget) {
    Write-Step 'Creating the monthly cost budget'
    Write-Skipped 'Skipped (-SkipBudget).'
}
else {
    Write-Step "Creating a USD $MonthlyBudget/month budget alert"

    $today = [datetime]::UtcNow
    $budgetStart = [datetime]::new($today.Year, $today.Month, 1, 0, 0, 0, [System.DateTimeKind]::Utc)

    function New-BudgetNotification {
        param([Parameter(Mandatory)][int]$Threshold, [Parameter(Mandatory)][string]$Email)
        return [ordered]@{
            enabled       = $true
            operator      = 'GreaterThan'
            threshold     = $Threshold
            contactEmails = @($Email)
            thresholdType = 'Actual'
        }
    }

    $budgetBody = [ordered]@{
        properties = [ordered]@{
            category      = 'Cost'
            amount        = $MonthlyBudget
            timeGrain     = 'Monthly'
            timePeriod    = [ordered]@{ startDate = $budgetStart.ToString('yyyy-MM-ddTHH:mm:ssZ') }
            notifications = [ordered]@{
                'Actual_GreaterThan_50_Percent'  = New-BudgetNotification -Threshold 50 -Email $BudgetAlertEmail
                'Actual_GreaterThan_80_Percent'  = New-BudgetNotification -Threshold 80 -Email $BudgetAlertEmail
                'Actual_GreaterThan_100_Percent' = New-BudgetNotification -Threshold 100 -Email $BudgetAlertEmail
            }
        }
    } | ConvertTo-Json -Depth 10

    $budgetBodyPath = [System.IO.Path]::GetTempFileName()
    try {
        Set-Content -LiteralPath $budgetBodyPath -Value $budgetBody -Encoding utf8NoBOM
        $budgetUrl = "https://management.azure.com/subscriptions/$SubscriptionId/providers/Microsoft.Consumption/budgets/$budgetName" +
        '?api-version=2023-05-01'

        $budgetResult = Invoke-Az -IgnoreErrors -AsJson -Arguments @(
            'rest', '--method', 'put',
            '--url', $budgetUrl,
            '--headers', 'Content-Type=application/json',
            '--body', "@$budgetBodyPath")

        if ($budgetResult) {
            Write-Detail "Alerts at 50%, 80%, and 100% of USD $MonthlyBudget go to $BudgetAlertEmail."
        }
        else {
            Write-Skipped 'Budget creation failed, which is common without billing-writer rights. Create it under Cost Management > Budgets.'
        }
    }
    finally {
        Remove-Item -LiteralPath $budgetBodyPath -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------------------------
# Post-deploy verification
# ---------------------------------------------------------------------------------------------

if ($SkipFunctionApp) {
    Write-Step 'Verifying the deployment'
    Write-Skipped 'Skipped (-SkipFunctionApp).'
}
else {
    Write-Step 'Verifying the deployment against /api/health'

    # HealthCheckFunction is anonymous and reports database reachability and pending migrations,
    # which is the difference between "deployed" and "actually working". Without this the first
    # real signal would be a failed timer tick, potentially a whole session away.
    $healthUrl = "https://$functionAppName.azurewebsites.net/api/health"
    $healthy = $false

    # A cold Flex Consumption app plus a just-restarted host can take a while to answer the first
    # request, so poll rather than judging the deployment on a single attempt.
    foreach ($attempt in 1..5) {
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -Method Get -TimeoutSec 60 -SkipHttpErrorCheck
            $payload = $null
            try { $payload = $response.Content | ConvertFrom-Json } catch { }

            if ($response.StatusCode -eq 200) {
                Write-Detail "Healthy. Monitored symbols: $($payload.monitoredSymbolCount)."
                $healthy = $true
                break
            }

            if ($payload) {
                $pending = @($payload.database.pendingMigrations)
                Write-Detail "Attempt $attempt returned HTTP $($response.StatusCode): database reachable = $($payload.database.reachable), pending migrations = $($pending.Count)."
            }
            else {
                Write-Detail "Attempt $attempt returned HTTP $($response.StatusCode)."
            }
        }
        catch {
            Write-Detail "Attempt $attempt failed: $($_.Exception.Message)"
        }

        if ($attempt -lt 5) { Start-Sleep -Seconds 15 }
    }

    if (-not $healthy) {
        Write-Skipped "The health endpoint did not report healthy. Inspect it directly: $healthUrl"
        Write-Skipped "Then check logs: az webapp log tail --name $functionAppName --resource-group $ResourceGroup"
    }
}

# ---------------------------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------------------------

Write-Host ''
Write-Host 'Deployment complete.' -ForegroundColor Green
Write-Host ''
Write-Host '  Resource group : ' -NoNewline; Write-Host $ResourceGroup
Write-Host '  Function App   : ' -NoNewline; Write-Host "https://$functionAppName.azurewebsites.net"
Write-Host '  Health         : ' -NoNewline; Write-Host "https://$functionAppName.azurewebsites.net/api/health"
if ($staticWebAppHostname) {
    Write-Host '  Web UI         : ' -NoNewline; Write-Host "https://$staticWebAppHostname"
}
Write-Host '  Database       : ' -NoNewline; Write-Host "$sqlServerFqdn / $sqlDatabaseName"
Write-Host ''
Write-Host '  Stream logs    : ' -NoNewline; Write-Host "az webapp log tail --name $functionAppName --resource-group $ResourceGroup"
Write-Host '  Tear down all  : ' -NoNewline; Write-Host "az group delete --name $ResourceGroup --yes"
Write-Host ''
Write-Host 'Before sharing the web URL with anyone:' -ForegroundColor Yellow
Write-Host '  portfolioweb/src/lib/auth.js is still MOCK authentication. It checks a hardcoded' -ForegroundColor Yellow
Write-Host '  username and password in the browser, and the deployed bundle carries the function' -ForegroundColor Yellow
Write-Host '  key, so anyone with the URL can reach the import API. Treat this deployment as' -ForegroundColor Yellow
Write-Host '  private until real authentication is in place.' -ForegroundColor Yellow
Write-Host ''

