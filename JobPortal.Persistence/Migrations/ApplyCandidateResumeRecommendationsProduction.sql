BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810090000_AddCandidateResumeRecommendations'
)
BEGIN
    CREATE TABLE [CandidateResumeProfiles] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ExtractionStatus] int NOT NULL,
        [SkillsJson] nvarchar(4000) NOT NULL,
        [RoleKeywordsJson] nvarchar(2000) NOT NULL,
        [EducationKeywordsJson] nvarchar(2000) NOT NULL,
        [LocationsJson] nvarchar(2000) NOT NULL,
        [YearsOfExperience] decimal(4,1) NULL,
        [ExtractionError] nvarchar(1000) NULL,
        [ExtractedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_CandidateResumeProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CandidateResumeProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810090000_AddCandidateResumeRecommendations'
)
BEGIN
    CREATE INDEX [IX_CandidateResumeProfiles_ExtractionStatus_ExtractedAtUtc] ON [CandidateResumeProfiles] ([ExtractionStatus], [ExtractedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810090000_AddCandidateResumeRecommendations'
)
BEGIN
    CREATE INDEX [IX_CandidateResumeProfiles_IsDeleted] ON [CandidateResumeProfiles] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810090000_AddCandidateResumeRecommendations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CandidateResumeProfiles_UserId] ON [CandidateResumeProfiles] ([UserId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810090000_AddCandidateResumeRecommendations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810090000_AddCandidateResumeRecommendations', N'9.0.8');
END;

COMMIT;
GO

