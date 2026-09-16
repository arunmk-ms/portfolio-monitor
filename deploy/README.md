# Deploying portfolio-monitor to Azure

`Deploy-PortfolioMonitor.ps1` provisions everything both apps need and pushes the code straight from
your machine. There is no build pipeline, no service principal, and no secret in source control.

The script is idempotent. Run it again after a failure or a code change and it updates in place
rather than creating duplicates.

## What it creates

| Resource | Purpose | Approx. cost/month |
| --- | --- | --- |
| Storage account (Standard_LRS) | Required by the Functions runtime | ~$1 |
| Log Analytics + Application Insights | Telemetry; first 5 GB/month are free | ~$0 |
| Azure SQL Database, Basic (5 DTU, 2 GB) | `PortfolioDb` | ~$5 |
| Function App, Flex Consumption | `portfolioapp` timer trigger | ~$0–2 |
| Static Web App, Free tier | `portfolioweb` | $0 |
| Consumption budget | Alert-only guard rail, default $30 | $0 |

Expect roughly **$6–10/month**, comfortably inside a $150 cap.

## Prerequisites

- PowerShell 7+
- Azure CLI, signed in with access to the target subscription
- .NET 8 SDK and `dotnet ef` (`dotnet tool install --global dotnet-ef`)
- Azure Functions Core Tools v4 (`npm install -g azure-functions-core-tools@4 --unsafe-perm true`)
- Node.js LTS
- A free [Alpha Vantage API key](https://www.alphavantage.co/support/#api-key)

## First run

```powershell
cd deploy
./Deploy-PortfolioMonitor.ps1 -SubscriptionId '<your-personal-subscription-id>'
```

You are prompted for the Alpha Vantage key, which goes straight into Function App settings and is
never written to disk. Find the subscription ID with `az account list --output table`.

Useful switches:

```powershell
# Different region, symbols, and budget
./Deploy-PortfolioMonitor.ps1 -SubscriptionId '...' -Location westus2 `
    -Symbols 'MSFT,NVDA,AAPL' -MonthlyBudget 25

# Redeploy code only, leaving infrastructure and schema alone
./Deploy-PortfolioMonitor.ps1 -SubscriptionId '...' -SkipInfrastructure -SkipDatabase

# Ship only the web UI
./Deploy-PortfolioMonitor.ps1 -SubscriptionId '...' -SkipInfrastructure -SkipDatabase -SkipFunctionApp -SkipBudget
```

Resource names are derived from a hash of the subscription, resource group, and `-NamePrefix`, so
the same inputs always resolve to the same resources. Changing any of them targets a new set.

## How authentication works

No database password exists anywhere in this design.

- The SQL server is created with **Entra-only authentication** and you are its admin.
- The Function App gets a **system-assigned managed identity**, which the script adds to the
  database as a contained user in `db_datareader` and `db_datawriter`.
- Its connection string uses `Authentication=Active Directory Managed Identity`, so it holds no
  credential.
- Migrations are applied from your machine over an access token from `az account get-access-token`.

The only secret in play is the Alpha Vantage key, which lives in Function App settings. `Program.cs`
deliberately keeps the `HttpClient` log categories at `Warning` because that provider only accepts
its key as a query-string parameter.

## Telemetry

`host.json` runs the Functions host in OpenTelemetry mode, and `Program.cs` picks worker exporters
from configuration:

- `APPLICATIONINSIGHTS_CONNECTION_STRING` set → export to Application Insights (what the script
  configures).
- `OTEL_EXPORTER_OTLP_ENDPOINT` set → export over OTLP, which is how `local.settings.json` targets a
  local collector.

Both can be set at once. Neither is registered when unset, which matters because `UseOtlpExporter()`
silently falls back to `http://localhost:4318` and would otherwise retry against nothing forever.

## Schedule

`QuoteMonitorSchedule` defaults to `0 */15 13-21 * * 1-5` — every 15 minutes, 13:00–21:00 UTC,
weekdays. Linux Function Apps evaluate NCRONTAB in UTC, so that window tracks the US session during
EDT and runs an hour late during EST. This is only a coarse filter; `UsEquityMarketCalendar` makes
the real open/close/holiday decision, so an off-by-an-hour window costs nothing but a few no-op
invocations.

## How the web UI reaches the API

`portfolioapp` exposes two HTTP endpoints alongside the timer:

| Route | Auth | Purpose |
| --- | --- | --- |
| `GET /api/health` | Anonymous | Readiness probe; reports database reachability and pending migrations |
| `POST /api/portfolio/import` | Function key | Accepts a broker positions CSV |

The SPA and the API sit on different hosts, so the script wires them together:

1. The Static Web App is created **before** the Function App is configured, so its hostname is
   known.
2. That origin is added to Function App CORS. The `x-functions-key` header makes every upload a
   preflighted request, so without this the browser blocks the call before it is sent.
3. After publishing, the script reads the default host key and builds the SPA with
   `VITE_API_BASE_URL` and `VITE_API_FUNCTION_KEY` set, which Vite inlines at build time.

Because the key is inlined, **rebuild and redeploy the front end whenever you rotate the function
key**, otherwise the deployed UI keeps sending the old one.

## Security status — read before sharing the URL

This deployment is **not ready to be public**. Two things are outstanding:

- **Authentication is mocked.** `portfolioweb/src/lib/auth.js` validates a hardcoded username and
  password in the browser. The file says so itself. Anyone who loads the page can read the
  credentials and sign in.
- **The function key ships in the JS bundle.** That is inherent to calling a key-protected API from
  a static site. Anyone with the URL can extract it and POST to the import endpoint.

Neither blocks a deployment you keep to yourself, and the Static Web App URL is an unguessable
hostname. But treat it as private, and do not put real brokerage data behind it until the auth story
is real. The realistic fix is Entra Easy Auth on the Function App with a proper sign-in in the SPA,
which also removes the need for the embedded key.

## Verification

The script polls `GET /api/health` after publishing and prints the result, so a deployment whose
migrations never applied fails loudly instead of looking fine until the next timer tick. Check it
manually any time:

```powershell
curl https://<function-app>.azurewebsites.net/api/health
```

A healthy response is `200` with `"status": "healthy"` and an empty `pendingMigrations` array.

## Operating it

```powershell
# Live logs
az webapp log tail --name <function-app> --resource-group rg-portfolio-monitor

# Confirm the timer is registered
az functionapp function list --name <function-app> --resource-group rg-portfolio-monitor --output table

# Month-to-date spend
az consumption usage list --output table

# Remove everything
az group delete --name rg-portfolio-monitor --yes
```

## Troubleshooting

**`Could not read the signed-in user's object ID`** — the CLI is signed in to a different tenant.
Run `az login --tenant <tenant-id>` for the tenant that owns the subscription.

**Migrations fail with a login or firewall error** — your public IP changed since the last run. The
script re-detects it each time, so rerun with `-SkipFunctionApp -SkipWeb`, or pass
`-ClientIpAddress <ip>` if you are behind a proxy that hides it.

**`Region '<x>' does not offer Flex Consumption`** — the error lists the regions that do. Pick one
and pass it as `-Location`.

**Budget creation is skipped** — creating budgets needs billing-writer rights, which some
subscriptions withhold. Everything else still deploys; add the budget under Cost Management →
Budgets in the portal.

## Cost notes

Azure SQL Basic is the predictable choice at a flat ~$5/month. Serverless General Purpose with
auto-pause can be cheaper if the app runs rarely, but this timer keeps the database warm through the
whole trading session, which is exactly the pattern that makes serverless cost *more*. Revisit only
if the schedule becomes much sparser.

Storage is the other thing worth watching. `SqlQuoteStore` already skips duplicate snapshots, so the
2 GB Basic ceiling lasts a long time at a handful of symbols.
