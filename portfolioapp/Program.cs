using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using PortfolioApp.Configuration;
using PortfolioApp.Data;
using PortfolioApp.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureLogging(logging =>
    {
        // Alpha Vantage only accepts the API key as a query-string parameter, and the default
        // HttpClient logging writes the full request URI at Information level. Keep those two
        // categories at Warning so the key never reaches the logs or any telemetry backend.
        logging.AddFilter($"System.Net.Http.HttpClient.{AlphaVantageClient.HttpClientName}.LogicalHandler", LogLevel.Warning);
        logging.AddFilter($"System.Net.Http.HttpClient.{AlphaVantageClient.HttpClientName}.ClientHandler", LogLevel.Warning);
    })
    .ConfigureServices((context, services) => {
        // Worker telemetry exporters are selected by configuration so one build serves both a
        // local collector and Azure. host.json opts the Functions host into OpenTelemetry too,
        // and the host honours the same two settings.
        var telemetry = services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

        // UseOtlpExporter() defaults to http://localhost:4318 when no endpoint is configured,
        // where it would retry against nothing for the lifetime of the process. Register it only
        // when an endpoint actually exists.
        if (!string.IsNullOrWhiteSpace(context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        var appInsightsConnectionString = context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            telemetry.UseAzureMonitorExporter(options => options.ConnectionString = appInsightsConnectionString);
        }

        services.AddSingleton(TimeProvider.System);

        // Fail fast when the store isn't configured: silently discarding fetched quotes is the
        // exact failure mode this persistence layer exists to prevent.
        var connectionString = context.Configuration.GetConnectionString("PortfolioDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing connection string 'PortfolioDb'. Set the 'ConnectionStrings__PortfolioDb' application setting.");
        }

        services.AddDbContext<PortfolioDbContext>(builder =>
            builder.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        services.AddScoped<IQuoteStore, SqlQuoteStore>();
        services.AddScoped<IPositionsCsvParser, FidelityPositionsCsvParser>();
        services.AddScoped<IPortfolioImportService, PortfolioImportService>();
        services.AddScoped<IManualHoldingService, ManualHoldingService>();
        services.AddScoped<IWatchlistProvider, WatchlistProvider>();

        services.AddSingleton<IRuleEngine, RuleEngine>();
        services.AddScoped<ISignalStore, SqlSignalStore>();
        services.AddScoped<IPortfolioAnalysisService, PortfolioAnalysisService>();

        // Registered as a collection so additional channels (email, push) can be added without
        // touching the analysis service.
        services.AddScoped<INotificationChannel, LoggingNotificationChannel>();

        services.AddOptions<AlphaVantageOptions>()
            .Bind(context.Configuration.GetSection(AlphaVantageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MarketHoursOptions>()
            .Bind(context.Configuration.GetSection(MarketHoursOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IMarketCalendar, UsEquityMarketCalendar>();

        services.AddHttpClient<IAlphaVantageClient, AlphaVantageClient>(
            AlphaVantageClient.HttpClientName,
            (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<AlphaVantageOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("portfolio-monitor/1.0");
            });
    })
    .Build();

host.Run();
