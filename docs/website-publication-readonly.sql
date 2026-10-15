BEGIN TRANSACTION READ ONLY;
SELECT "Id", "Status", "PublishedAtUtc", "CreatedAtUtc", "SourceOwnsPublication",
       "AcquisitionSourceId", "JobSourceId", "Location", "CategoryId",
       "DiscoveredUrl", "CanonicalJobUrl", "SourceMetadataJson"
FROM "Jobs" WHERE "Id" = 'ca1c769b-d0c2-44a6-bb98-322a9f94bb6f';
SELECT "Id", "ProviderKey", "IsEnabled", "AutoPublishEnabled", "IsDeleted"
FROM "AcquisitionSources" WHERE "Id" = 'e7416f86-454a-4b59-9331-379956e36d92';
SELECT * FROM "AcquisitionImportRuns"
WHERE "BatchId" = 'ch-v1-84ebd5d1849533e92bccd68f46788a6a82231c5b9f5f04905daabf149f1e4eef';
SELECT * FROM "AcquisitionImportItems"
WHERE "JobId" = 'ca1c769b-d0c2-44a6-bb98-322a9f94bb6f';
SELECT "CreatedAtUtc", "Action", "EntityName", "EntityId", "UserId", "ActorRole", "CorrelationId", "ChangesJson"
FROM "AuditLogs" WHERE "EntityId" = 'ca1c769b-d0c2-44a6-bb98-322a9f94bb6f'
ORDER BY "CreatedAtUtc";
ROLLBACK;
