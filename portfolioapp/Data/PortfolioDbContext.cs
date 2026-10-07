using Microsoft.EntityFrameworkCore;
using PortfolioApp.Models;

namespace PortfolioApp.Data;

public sealed class PortfolioDbContext : DbContext
{
    public PortfolioDbContext(DbContextOptions<PortfolioDbContext> options)
        : base(options)
    {
    }

    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<PortfolioImport> PortfolioImports => Set<PortfolioImport>();

    public DbSet<Holding> Holdings => Set<Holding>();

    public DbSet<WatchlistSymbol> WatchlistSymbols => Set<WatchlistSymbol>();

    public DbSet<WatchRule> WatchRules => Set<WatchRule>();

    public DbSet<Signal> Signals => Set<Signal>();

    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigurePriceSnapshots(modelBuilder);
        ConfigurePortfolio(modelBuilder);
        ConfigureRules(modelBuilder);
    }

    private static void ConfigureRules(ModelBuilder modelBuilder)
    {
        var rule = modelBuilder.Entity<WatchRule>();
        rule.ToTable("WatchRules");
        rule.HasKey(e => e.Id);
        rule.Property(e => e.Name).IsRequired().HasMaxLength(128);
        rule.Property(e => e.Symbol).HasMaxLength(16);
        // Enums are stored as strings so the tables stay readable and a reordered enum member
        // cannot silently change the meaning of existing rows.
        rule.Property(e => e.RuleType).HasConversion<string>().HasMaxLength(48);
        rule.Property(e => e.Direction).HasConversion<string>().HasMaxLength(24);
        rule.Property(e => e.Threshold).HasColumnType("decimal(18,4)");
        rule.Ignore(e => e.RequiresHistory);
        rule.HasIndex(e => new { e.Symbol, e.IsEnabled }).HasDatabaseName("IX_WatchRules_Symbol_IsEnabled");

        var signal = modelBuilder.Entity<Signal>();
        signal.ToTable("Signals");
        signal.HasKey(e => e.Id);
        signal.Property(e => e.Symbol).IsRequired().HasMaxLength(16);
        signal.Property(e => e.Direction).HasConversion<string>().HasMaxLength(24);
        signal.Property(e => e.Status).HasConversion<string>().HasMaxLength(24);
        signal.Property(e => e.Score).HasColumnType("decimal(9,4)");
        signal.Property(e => e.FactsJson).IsRequired();

        signal.HasOne(e => e.WatchRule)
            .WithMany()
            .HasForeignKey(e => e.WatchRuleId)
            // Deleting a rule must not erase the history of what it already reported.
            .OnDelete(DeleteBehavior.Restrict);

        // Backs the dedup lookup: "is there an open signal for this symbol and rule?"
        signal.HasIndex(e => new { e.Symbol, e.WatchRuleId, e.Status })
            .HasDatabaseName("IX_Signals_Symbol_WatchRuleId_Status");

        var alert = modelBuilder.Entity<Alert>();
        alert.ToTable("Alerts");
        alert.HasKey(e => e.Id);
        alert.Property(e => e.Channel).HasConversion<string>().HasMaxLength(24);
        alert.Property(e => e.FailureReason).HasMaxLength(512);

        alert.HasOne(e => e.Signal)
            .WithMany(s => s.Alerts)
            .HasForeignKey(e => e.SignalId)
            .OnDelete(DeleteBehavior.Cascade);

        alert.HasIndex(e => e.SignalId).HasDatabaseName("IX_Alerts_SignalId");
    }

    private static void ConfigurePortfolio(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<Account>();
        account.ToTable("Accounts");
        account.HasKey(e => e.Id);
        account.Property(e => e.Name).IsRequired().HasMaxLength(128);
        // Accounts are identified by name because the broker's account number is intentionally
        // never ingested; the unique index keeps repeated imports from duplicating accounts.
        account.HasIndex(e => e.Name).IsUnique().HasDatabaseName("IX_Accounts_Name");

        var import = modelBuilder.Entity<PortfolioImport>();
        import.ToTable("PortfolioImports");
        import.HasKey(e => e.Id);
        import.Property(e => e.FileName).IsRequired().HasMaxLength(260);
        import.HasIndex(e => e.ImportedAt).HasDatabaseName("IX_PortfolioImports_ImportedAt");

        var holding = modelBuilder.Entity<Holding>();
        holding.ToTable("Holdings");
        holding.HasKey(e => e.Id);
        holding.Property(e => e.Symbol).IsRequired().HasMaxLength(16);
        holding.Property(e => e.Description).HasMaxLength(256);
        holding.Property(e => e.PositionType).HasConversion<string>().HasMaxLength(16);

        foreach (var property in new[]
                 {
                     nameof(Holding.Quantity),
                     nameof(Holding.AverageCostBasis),
                     nameof(Holding.CostBasisTotal),
                     nameof(Holding.CurrentValue),
                     nameof(Holding.LastPrice)
                 })
        {
            holding.Property(property).HasColumnType("decimal(18,4)");
        }

        // One row per symbol per account: re-importing updates in place rather than appending.
        holding.HasIndex(e => new { e.AccountId, e.Symbol })
            .IsUnique()
            .HasDatabaseName("IX_Holdings_AccountId_Symbol");

        holding.HasOne(e => e.Account)
            .WithMany(a => a.Holdings)
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // Keep import history immutable: deleting an audit row must not silently drop holdings.
        // The key is optional because a manually entered holding has no originating import.
        holding.HasOne(e => e.LastImport)
            .WithMany(i => i.Holdings)
            .HasForeignKey(e => e.LastImportId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        holding.Ignore(e => e.IsManual);

        var watchlist = modelBuilder.Entity<WatchlistSymbol>();
        watchlist.ToTable("WatchlistSymbols");
        watchlist.HasKey(e => e.Id);
        watchlist.Property(e => e.Symbol).IsRequired().HasMaxLength(16);
        watchlist.Property(e => e.Source).HasConversion<string>().HasMaxLength(16);
        watchlist.HasIndex(e => e.Symbol).IsUnique().HasDatabaseName("IX_WatchlistSymbols_Symbol");
    }

    private static void ConfigurePriceSnapshots(ModelBuilder modelBuilder)
    {
        var snapshot = modelBuilder.Entity<PriceSnapshot>();

        snapshot.ToTable("PriceSnapshots");
        snapshot.HasKey(e => e.Id);

        snapshot.Property(e => e.Symbol)
            .IsRequired()
            .HasMaxLength(16);

        // Money values: 18,4 leaves room for high-priced tickers without float drift.
        foreach (var property in new[]
                 {
                     nameof(PriceSnapshot.Price),
                     nameof(PriceSnapshot.Open),
                     nameof(PriceSnapshot.High),
                     nameof(PriceSnapshot.Low),
                     nameof(PriceSnapshot.PreviousClose),
                     nameof(PriceSnapshot.Change)
                 })
        {
            snapshot.Property(property).HasColumnType("decimal(18,4)");
        }

        snapshot.Property(e => e.ChangePercent).HasColumnType("decimal(9,4)");

        // Covers the dominant read pattern: the most recent N snapshots for one symbol,
        // which is what moving averages and RSI need.
        snapshot.HasIndex(e => new { e.Symbol, e.CapturedAt })
            .HasDatabaseName("IX_PriceSnapshots_Symbol_CapturedAt");
    }
}
