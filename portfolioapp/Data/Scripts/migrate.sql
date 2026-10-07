IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910033252_InitialCreate'
)
BEGIN
    CREATE TABLE [PriceSnapshots] (
        [Id] bigint NOT NULL IDENTITY,
        [Symbol] nvarchar(16) NOT NULL,
        [Price] decimal(18,4) NOT NULL,
        [Open] decimal(18,4) NOT NULL,
        [High] decimal(18,4) NOT NULL,
        [Low] decimal(18,4) NOT NULL,
        [PreviousClose] decimal(18,4) NOT NULL,
        [Change] decimal(18,4) NOT NULL,
        [ChangePercent] decimal(9,4) NOT NULL,
        [Volume] bigint NOT NULL,
        [LatestTradingDay] date NULL,
        [CapturedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PriceSnapshots] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910033252_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PriceSnapshots_Symbol_CapturedAt] ON [PriceSnapshots] ([Symbol], [CapturedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910033252_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260910033252_InitialCreate', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    CREATE TABLE [Accounts] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(128) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Accounts] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    CREATE TABLE [PortfolioImports] (
        [Id] int NOT NULL IDENTITY,
        [FileName] nvarchar(260) NOT NULL,
        [SourceDownloadedAt] datetimeoffset NULL,
        [ImportedAt] datetimeoffset NOT NULL,
        [RowsParsed] int NOT NULL,
        [PositionsImported] int NOT NULL,
        [RowsSkipped] int NOT NULL,
        CONSTRAINT [PK_PortfolioImports] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    CREATE TABLE [WatchlistSymbols] (
        [Id] int NOT NULL IDENTITY,
        [Symbol] nvarchar(16) NOT NULL,
        [Source] nvarchar(16) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [AddedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_WatchlistSymbols] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    CREATE TABLE [Holdings] (
        [Id] int NOT NULL IDENTITY,
        [AccountId] int NOT NULL,
        [Symbol] nvarchar(16) NOT NULL,
        [Description] nvarchar(256) NULL,
        [Quantity] decimal(18,4) NULL,
        [AverageCostBasis] decimal(18,4) NULL,
        [CostBasisTotal] decimal(18,4) NULL,
        [CurrentValue] decimal(18,4) NULL,
        [LastPrice] decimal(18,4) NULL,
        [PositionType] nvarchar(16) NOT NULL,
        [IsMonitorable] bit NOT NULL,
        [FirstSeenAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [LastImportId] int NOT NULL,
        CONSTRAINT [PK_Holdings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Holdings_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [Accounts] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Holdings_PortfolioImports_LastImportId] FOREIGN KEY ([LastImportId]) REFERENCES [PortfolioImports] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Accounts_Name] ON [Accounts] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Holdings_AccountId_Symbol] ON [Holdings] ([AccountId], [Symbol]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    CREATE INDEX [IX_Holdings_LastImportId] ON [Holdings] ([LastImportId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    CREATE INDEX [IX_PortfolioImports_ImportedAt] ON [PortfolioImports] ([ImportedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WatchlistSymbols_Symbol] ON [WatchlistSymbols] ([Symbol]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910220807_AddPortfolioAndWatchlist'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260910220807_AddPortfolioAndWatchlist', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916180620_AddRulesSignalsAlerts'
)
BEGIN
    CREATE TABLE [WatchRules] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(128) NOT NULL,
        [Symbol] nvarchar(16) NULL,
        [RuleType] nvarchar(48) NOT NULL,
        [Threshold] decimal(18,4) NOT NULL,
        [Period] int NULL,
        [Direction] nvarchar(24) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [Version] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_WatchRules] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916180620_AddRulesSignalsAlerts'
)
BEGIN
    CREATE TABLE [Signals] (
        [Id] int NOT NULL IDENTITY,
        [Symbol] nvarchar(16) NOT NULL,
        [WatchRuleId] int NOT NULL,
        [RuleVersion] int NOT NULL,
        [Direction] nvarchar(24) NOT NULL,
        [Score] decimal(9,4) NOT NULL,
        [FactsJson] nvarchar(max) NOT NULL,
        [Status] nvarchar(24) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ResolvedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Signals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Signals_WatchRules_WatchRuleId] FOREIGN KEY ([WatchRuleId]) REFERENCES [WatchRules] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916180620_AddRulesSignalsAlerts'
)
BEGIN
    CREATE TABLE [Alerts] (
        [Id] int NOT NULL IDENTITY,
        [SignalId] int NOT NULL,
        [Channel] nvarchar(24) NOT NULL,
        [SentAt] datetimeoffset NULL,
        [AcknowledgedAt] datetimeoffset NULL,
        [FailureReason] nvarchar(512) NULL,
        CONSTRAINT [PK_Alerts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Alerts_Signals_SignalId] FOREIGN KEY ([SignalId]) REFERENCES [Signals] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916180620_AddRulesSignalsAlerts'
)
BEGIN
    CREATE INDEX [IX_Alerts_SignalId] ON [Alerts] ([SignalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916180620_AddRulesSignalsAlerts'
)
BEGIN
    CREATE INDEX [IX_Signals_Symbol_WatchRuleId_Status] ON [Signals] ([Symbol], [WatchRuleId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916180620_AddRulesSignalsAlerts'
)
BEGIN
    CREATE INDEX [IX_Signals_WatchRuleId] ON [Signals] ([WatchRuleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916180620_AddRulesSignalsAlerts'
)
BEGIN
    CREATE INDEX [IX_WatchRules_Symbol_IsEnabled] ON [WatchRules] ([Symbol], [IsEnabled]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260916180620_AddRulesSignalsAlerts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260916180620_AddRulesSignalsAlerts', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918172132_AddManualHoldings'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Holdings]') AND [c].[name] = N'LastImportId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Holdings] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Holdings] ALTER COLUMN [LastImportId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918172132_AddManualHoldings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918172132_AddManualHoldings', N'8.0.31');
END;
GO

COMMIT;
GO

