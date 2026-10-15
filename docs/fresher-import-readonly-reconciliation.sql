-- Read-only reconciliation; no import, migration, update, or deletion.
BEGIN TRANSACTION READ ONLY;

SELECT "MigrationId"
FROM "__EFMigrationsHistory"
WHERE "MigrationId" IN (
  '20261014212715_AddFresherAcquisitionFoundation',
  '20261014214058_AddFresherRunRejectionCounters',
  '20261014214340_AddNormalizedCompanyWebsiteDomain',
  '20261015040449_HardenFresherAcquisitionRecovery',
  '20261015040944_AddFresherPublicationRetryState')
ORDER BY "MigrationId";

SELECT "Id", "ProviderKey", "Name", "IsEnabled", "AutoPublishEnabled", "IsDeleted"
FROM "AcquisitionSources" WHERE "ProviderKey" = 'serpapi-google-jobs';

SELECT "Id", "AcquisitionSourceId", "BatchId", "Status", "StartedAtUtc", "CompletedAtUtc",
       "ReceivedCount", "CreatedCount", "UpdatedCount", "ExistingCount", "FailedCount", "PublishedCount"
FROM "AcquisitionImportRuns"
WHERE "BatchId" = 'ch-v1-03d13df0fb5a5b6fbaa601a4ffd391ca1f340524ca87ba63e8bcc02126a745b6';

SELECT i."AcquisitionImportRunId", i."Outcome", i."ReasonCode", i."PublicationStatus",
       COUNT(*) AS "ReceiptCount", COUNT(i."JobId") AS "LinkedJobCount"
FROM "AcquisitionImportItems" i
JOIN "AcquisitionImportRuns" r ON r."Id" = i."AcquisitionImportRunId"
WHERE r."BatchId" = 'ch-v1-03d13df0fb5a5b6fbaa601a4ffd391ca1f340524ca87ba63e8bcc02126a745b6'
  AND NOT i."IsDeleted"
GROUP BY i."AcquisitionImportRunId", i."Outcome", i."ReasonCode", i."PublicationStatus";

COMMIT;
