BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811090000_AddCandidateAppliedJobs'
)
BEGIN
    ALTER TABLE [JobApplications] ADD [ApplicationMethod] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811090000_AddCandidateAppliedJobs'
)
BEGIN
    DROP INDEX [IX_JobApplications_UserId_JobId] ON [JobApplications];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811090000_AddCandidateAppliedJobs'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_JobApplications_UserId_JobId] ON [JobApplications] ([UserId], [JobId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811090000_AddCandidateAppliedJobs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811090000_AddCandidateAppliedJobs', N'9.0.8');
END;

COMMIT;
GO

