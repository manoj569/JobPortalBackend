BEGIN TRANSACTION;

IF NOT EXISTS (
    SELECT 1
    FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260809150000_AddJobDiscovery'
)
BEGIN
    CREATE TABLE [JobDiscoveryRuns] (
        [Id] uniqueidentifier NOT NULL,
        [Trigger] nvarchar(32) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        [CandidateCount] int NOT NULL,
        [DuplicateCount] int NOT NULL,
        [ImportedCount] int NOT NULL,
        [ErrorSummary] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_JobDiscoveryRuns] PRIMARY KEY ([Id])
    );

    CREATE TABLE [JobDiscoveryItems] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [Provider] nvarchar(64) NOT NULL,
        [SourceJobId] nvarchar(256) NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [CompanyName] nvarchar(200) NOT NULL,
        [CategoryName] nvarchar(200) NOT NULL,
        [ApplicationUrl] nvarchar(2048) NOT NULL,
        [Location] nvarchar(300) NULL,
        [Description] nvarchar(max) NULL,
        [EmploymentType] nvarchar(50) NULL,
        [PublishedAtUtc] datetime2 NULL,
        [Status] nvarchar(32) NOT NULL,
        [DuplicateReason] nvarchar(64) NULL,
        [ExistingJobId] uniqueidentifier NULL,
        [ImportedJobId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_JobDiscoveryItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_JobDiscoveryItems_JobDiscoveryRuns_RunId]
            FOREIGN KEY ([RunId])
            REFERENCES [JobDiscoveryRuns] ([Id])
            ON DELETE CASCADE
    );

    CREATE INDEX [IX_JobDiscoveryRuns_StartedAtUtc]
        ON [JobDiscoveryRuns] ([StartedAtUtc]);

    CREATE INDEX [IX_JobDiscoveryItems_RunId]
        ON [JobDiscoveryItems] ([RunId]);

    CREATE UNIQUE INDEX [IX_JobDiscoveryItems_Provider_SourceJobId]
        ON [JobDiscoveryItems] ([Provider], [SourceJobId])
        WHERE [IsDeleted] = 0;

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260809150000_AddJobDiscovery', N'9.0.8');
END;

COMMIT;
GO