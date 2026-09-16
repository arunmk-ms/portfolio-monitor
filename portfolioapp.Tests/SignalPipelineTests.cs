using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortfolioApp.Data;
using PortfolioApp.Models;
using PortfolioApp.Services;
using Xunit;

namespace PortfolioApp.Tests;

/// <summary>
/// Covers the signal lifecycle and the analysis pipeline end to end: quotes and holdings in,
/// signals and alerts out.
/// </summary>
public sealed class SignalPipelineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PortfolioDbContext> _options;
    private DateTimeOffset _now = new(2026, 9, 16, 16, 0, 0, TimeSpan.Zero);

    public SignalPipelineTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options;

        using var context = new PortfolioDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private PortfolioDbContext NewContext() => new(_options);

    private async Task<WatchRule> SeedRuleAsync(
        RuleType type = RuleType.PriceBelow,
        decimal threshold = 100m,
        string? symbol = "MSFT",
        SignalDirection direction = SignalDirection.BuyCandidate,
        int version = 1,
        int? period = null)
    {
        await using var context = NewContext();
        var rule = new WatchRule
        {
            Name = $"{type} {threshold}",
            Symbol = symbol,
            RuleType = type,
            Threshold = threshold,
            Period = period,
            Direction = direction,
            IsEnabled = true,
            Version = version,
            CreatedAt = _now,
            UpdatedAt = _now
        };
        context.WatchRules.Add(rule);
        await context.SaveChangesAsync();
        return rule;
    }

    private async Task SeedHoldingAsync(string symbol, decimal quantity, decimal averageCost)
    {
        await using var context = NewContext();

        var account = await context.Accounts.FirstOrDefaultAsync(a => a.Name == "Test");
        if (account is null)
        {
            account = new Account { Name = "Test", CreatedAt = _now };
            context.Accounts.Add(account);
            await context.SaveChangesAsync();
        }

        var import = new PortfolioImport { FileName = "test.csv", ImportedAt = _now };
        context.PortfolioImports.Add(import);
        await context.SaveChangesAsync();

        context.Holdings.Add(new Holding
        {
            AccountId = account.Id,
            Symbol = symbol,
            Quantity = quantity,
            AverageCostBasis = averageCost,
            PositionType = PositionType.Equity,
            IsMonitorable = true,
            FirstSeenAt = _now,
            UpdatedAt = _now,
            LastImportId = import.Id
        });

        await context.SaveChangesAsync();
    }

    private static StockQuote Quote(string symbol, decimal price) =>
        new(symbol, price, price, price, price, price, 0m, 0m, 1_000L,
            new DateOnly(2026, 9, 16), new DateTimeOffset(2026, 9, 16, 16, 0, 0, TimeSpan.Zero));

    private async Task<AnalysisResult> AnalyseAsync(
        params StockQuote[] quotes)
    {
        await using var context = NewContext();
        var service = new PortfolioAnalysisService(
            context,
            new RuleEngine(),
            new SqlSignalStore(context, NullLogger<SqlSignalStore>.Instance),
            new INotificationChannel[] { new LoggingNotificationChannel(NullLogger<LoggingNotificationChannel>.Instance) },
            new FixedTime(_now),
            NullLogger<PortfolioAnalysisService>.Instance);

        return await service.AnalyseAsync(quotes, CancellationToken.None);
    }

    [Fact]
    public async Task Analysis_RaisesSignalWhenRuleTriggers()
    {
        await SeedRuleAsync(threshold: 100m);

        var result = await AnalyseAsync(Quote("MSFT", 90m));

        Assert.Equal(1, result.SignalsRaised);

        await using var context = NewContext();
        var signal = await context.Signals.SingleAsync();
        Assert.Equal("MSFT", signal.Symbol);
        Assert.Equal(SignalDirection.BuyCandidate, signal.Direction);
        Assert.Equal(SignalStatus.Open, signal.Status);
    }

    [Fact]
    public async Task Analysis_RaisesNothingWhenConditionIsFalse()
    {
        await SeedRuleAsync(threshold: 100m);

        var result = await AnalyseAsync(Quote("MSFT", 150m));

        Assert.Equal(0, result.SignalsRaised);
        await using var context = NewContext();
        Assert.Equal(0, await context.Signals.CountAsync());
    }

    /// <summary>The core of "suppress duplicate alerts until a condition resets".</summary>
    [Fact]
    public async Task Analysis_DoesNotRaiseDuplicateWhileConditionStaysTrue()
    {
        await SeedRuleAsync(threshold: 100m);

        await AnalyseAsync(Quote("MSFT", 90m));
        var second = await AnalyseAsync(Quote("MSFT", 85m));
        var third = await AnalyseAsync(Quote("MSFT", 80m));

        Assert.Equal(0, second.SignalsRaised);
        Assert.Equal(1, second.SignalsSuppressed);
        Assert.Equal(0, third.SignalsRaised);

        await using var context = NewContext();
        Assert.Equal(1, await context.Signals.CountAsync());
    }

    [Fact]
    public async Task Analysis_ResolvesSignalWhenConditionClears()
    {
        await SeedRuleAsync(threshold: 100m);
        await AnalyseAsync(Quote("MSFT", 90m));

        var result = await AnalyseAsync(Quote("MSFT", 120m));

        Assert.Equal(1, result.SignalsResolved);

        await using var context = NewContext();
        var signal = await context.Signals.SingleAsync();
        Assert.Equal(SignalStatus.Resolved, signal.Status);
        Assert.Equal(_now, signal.ResolvedAt);
    }

    [Fact]
    public async Task Analysis_ReArmsRuleAfterConditionResets()
    {
        await SeedRuleAsync(threshold: 100m);

        await AnalyseAsync(Quote("MSFT", 90m));   // raise
        await AnalyseAsync(Quote("MSFT", 120m));  // clear -> resolved
        var result = await AnalyseAsync(Quote("MSFT", 85m)); // should raise again

        Assert.Equal(1, result.SignalsRaised);

        await using var context = NewContext();
        Assert.Equal(2, await context.Signals.CountAsync());
        Assert.Equal(1, await context.Signals.CountAsync(s => s.Status == SignalStatus.Open));
        Assert.Equal(1, await context.Signals.CountAsync(s => s.Status == SignalStatus.Resolved));
    }

    /// <summary>
    /// Missing data must never be treated as "condition cleared", or the next run with data
    /// would re-alert on a condition the user already saw.
    /// </summary>
    [Fact]
    public async Task SignalStore_SkippedEvaluationDoesNotResolveAnOpenSignal()
    {
        var rule = await SeedRuleAsync(RuleType.RsiBelow, 30m);

        await using var context = NewContext();
        var store = new SqlSignalStore(context, NullLogger<SqlSignalStore>.Instance);
        var facts = new Dictionary<string, object?> { ["price"] = 90m };

        await store.RecordAsync("MSFT",
            new[] { new RuleEvaluation(rule, RuleOutcome.Triggered, 0.5m, facts) },
            _now, CancellationToken.None);

        var result = await store.RecordAsync("MSFT",
            new[] { new RuleEvaluation(rule, RuleOutcome.Skipped, 0m, facts, "not enough history") },
            _now, CancellationToken.None);

        Assert.Equal(0, result.Resolved);

        await using var verify = NewContext();
        Assert.Equal(SignalStatus.Open, (await verify.Signals.SingleAsync()).Status);
    }

    [Fact]
    public async Task Analysis_PersistsFactsExplainingTheSignal()
    {
        await SeedRuleAsync(threshold: 100m);

        await AnalyseAsync(Quote("MSFT", 90m));

        await using var context = NewContext();
        var signal = await context.Signals.SingleAsync();
        var facts = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(signal.FactsJson)!;

        Assert.Equal(90m, facts["price"].GetDecimal());
        Assert.Equal(100m, facts["threshold"].GetDecimal());
    }

    /// <summary>
    /// The rule version is copied onto the signal, so editing a rule cannot retroactively change
    /// what an already-raised alert claimed.
    /// </summary>
    [Fact]
    public async Task Analysis_SnapshotsRuleVersionOntoSignal()
    {
        var rule = await SeedRuleAsync(threshold: 100m, version: 3);
        await AnalyseAsync(Quote("MSFT", 90m));

        await using (var edit = NewContext())
        {
            var stored = await edit.WatchRules.SingleAsync(r => r.Id == rule.Id);
            stored.Version = 9;
            stored.Threshold = 10m;
            await edit.SaveChangesAsync();
        }

        await using var context = NewContext();
        Assert.Equal(3, (await context.Signals.SingleAsync()).RuleVersion);
    }

    [Fact]
    public async Task Analysis_RecordsAnAlertForEachRaisedSignal()
    {
        await SeedRuleAsync(threshold: 100m);

        var result = await AnalyseAsync(Quote("MSFT", 90m));

        Assert.Equal(1, result.AlertsSent);

        await using var context = NewContext();
        var alert = await context.Alerts.SingleAsync();
        Assert.Equal(AlertChannel.Log, alert.Channel);
        Assert.NotNull(alert.SentAt);
        Assert.Null(alert.FailureReason);
    }

    [Fact]
    public async Task Analysis_KeepsSignalWhenAlertDeliveryFails()
    {
        await SeedRuleAsync(threshold: 100m);

        await using var context = NewContext();
        var service = new PortfolioAnalysisService(
            context,
            new RuleEngine(),
            new SqlSignalStore(context, NullLogger<SqlSignalStore>.Instance),
            new INotificationChannel[] { new FailingChannel() },
            new FixedTime(_now),
            NullLogger<PortfolioAnalysisService>.Instance);

        var result = await service.AnalyseAsync(new[] { Quote("MSFT", 90m) }, CancellationToken.None);

        Assert.Equal(1, result.SignalsRaised);
        Assert.Equal(0, result.AlertsSent);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Signals.CountAsync());
        var alert = await verify.Alerts.SingleAsync();
        Assert.Null(alert.SentAt);
        Assert.Contains("channel down", alert.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analysis_UsesHoldingsForCostBasisRules()
    {
        await SeedHoldingAsync("MSFT", quantity: 10m, averageCost: 100m);
        await SeedRuleAsync(RuleType.PercentDropFromAverageCost, 15m);

        var result = await AnalyseAsync(Quote("MSFT", 80m));

        Assert.Equal(1, result.SignalsRaised);
    }

    [Fact]
    public async Task Analysis_ComputesAllocationAcrossHoldings()
    {
        // MSFT is 10 x 100 = 1000 of a 1250 portfolio => 80%.
        await SeedHoldingAsync("MSFT", quantity: 10m, averageCost: 50m);
        await SeedHoldingAsync("NVDA", quantity: 5m, averageCost: 40m);
        await SeedRuleAsync(RuleType.AllocationAbove, 50m, symbol: "MSFT", direction: SignalDirection.Rebalance);

        var result = await AnalyseAsync(Quote("MSFT", 100m), Quote("NVDA", 50m));

        Assert.Equal(1, result.SignalsRaised);

        await using var context = NewContext();
        var signal = await context.Signals.SingleAsync();
        var facts = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(signal.FactsJson)!;
        Assert.Equal(80m, facts["allocationPercent"].GetDecimal());
        Assert.Equal(SignalDirection.Rebalance, signal.Direction);
    }

    [Fact]
    public async Task Analysis_WeightsAverageCostAcrossAccounts()
    {
        // 10 @ 100 and 30 @ 200 => weighted average 175, not the simple mean of 150.
        await SeedHoldingAsync("MSFT", quantity: 10m, averageCost: 100m);

        await using (var second = NewContext())
        {
            var account = new Account { Name = "Second", CreatedAt = _now };
            second.Accounts.Add(account);
            var import = new PortfolioImport { FileName = "b.csv", ImportedAt = _now };
            second.PortfolioImports.Add(import);
            await second.SaveChangesAsync();

            second.Holdings.Add(new Holding
            {
                AccountId = account.Id,
                Symbol = "MSFT",
                Quantity = 30m,
                AverageCostBasis = 200m,
                PositionType = PositionType.Equity,
                IsMonitorable = true,
                FirstSeenAt = _now,
                UpdatedAt = _now,
                LastImportId = import.Id
            });
            await second.SaveChangesAsync();
        }

        await SeedRuleAsync(RuleType.PercentDropFromAverageCost, 10m);

        // 160 is an 8.6% drop from 175 (no trigger) but a 6.7% gain over 150 (would not trigger
        // either); use 150 which is a 14.3% drop from 175 and exactly 0% from the naive mean.
        var result = await AnalyseAsync(Quote("MSFT", 150m));

        Assert.Equal(1, result.SignalsRaised);

        await using var context = NewContext();
        var facts = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            (await context.Signals.SingleAsync()).FactsJson)!;
        Assert.Equal(175m, facts["averageCost"].GetDecimal());
    }

    [Fact]
    public async Task Analysis_DoesNothingWithoutEnabledRules()
    {
        var rule = await SeedRuleAsync(threshold: 100m);

        await using (var disable = NewContext())
        {
            var stored = await disable.WatchRules.SingleAsync(r => r.Id == rule.Id);
            stored.IsEnabled = false;
            await disable.SaveChangesAsync();
        }

        var result = await AnalyseAsync(Quote("MSFT", 10m));

        Assert.Equal(0, result.SignalsRaised);
    }

    [Fact]
    public async Task Analysis_TracksRulesIndependentlyPerSymbol()
    {
        await SeedRuleAsync(threshold: 100m, symbol: null);

        var result = await AnalyseAsync(Quote("MSFT", 90m), Quote("NVDA", 95m));

        Assert.Equal(2, result.SignalsRaised);

        await using var context = NewContext();
        Assert.Equal(1, await context.Signals.CountAsync(s => s.Symbol == "MSFT"));
        Assert.Equal(1, await context.Signals.CountAsync(s => s.Symbol == "NVDA"));
    }

    [Fact]
    public async Task Analysis_UsesStoredHistoryForIndicatorRules()
    {
        await SeedRuleAsync(RuleType.PriceBelowMovingAverage, 0m, period: 5);

        await using (var seed = NewContext())
        {
            for (var i = 0; i < 5; i++)
            {
                seed.PriceSnapshots.Add(new PriceSnapshot
                {
                    Symbol = "MSFT",
                    Price = 100m,
                    CapturedAt = _now.AddMinutes(-5 * (5 - i))
                });
            }
            await seed.SaveChangesAsync();
        }

        var result = await AnalyseAsync(Quote("MSFT", 50m));

        Assert.Equal(1, result.SignalsRaised);
    }

    private sealed class FailingChannel : INotificationChannel
    {
        public AlertChannel Channel => AlertChannel.Email;

        public Task SendAsync(Signal signal, WatchRule rule, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("channel down");
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTime(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
