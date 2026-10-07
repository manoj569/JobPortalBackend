# Deloitte India job source

CareerHarbor reads the official, public Deloitte India career board at
`https://southasiacareers.deloitte.com/go/Deloitte-India/718244`. The observed
site is SAP SuccessFactors and serves server-rendered HTML with 25-result offset
pagination. The provider reads the listing and public job detail pages; it keeps
the public job detail URL as `ApplicationUrl` and does not visit the separate
application endpoint.

The reusable `SuccessFactorsJobSourceProvider` now handles this configuration.
The existing Deloitte source URL, identifier, company, category mapping and scan
interval require no changes. See [SuccessFactors source configuration](successfactors-job-sources.md)
for supported templates and URL/transport safety limits.

## Database prerequisite

Apply the reviewed PostgreSQL migration
`AddDeloitteExternalJobSourceIdentity` before running this provider. It adds
nullable `Jobs.JobSourceId`, `Jobs.ExternalJobId`, and `Jobs.SourcePostedAtUtc`,
a restrictive FK to `JobSources`, and a filtered unique identity index. The
migration is additive and is not applied automatically by the application.

Create one source through the authenticated administrator endpoint
`POST /api/admin/job-sources`, using the existing Deloitte company ID:

```json
{
  "companyId": "<existing-deloitte-company-guid>",
  "careerPageUrl": "https://southasiacareers.deloitte.com/go/Deloitte-India/718244",
  "atsType": 4,
  "atsIdentifier": "718244",
  "isActive": true,
  "scanIntervalMinutes": 1440
}
```

The existing source category resolver is configuration-managed. Set
`JobAggregation__SourceCategories__<job-source-guid>` to the GUID of an
existing appropriate category; no categories are created by this provider.

## Scheduling and manual run

The existing scheduler is disabled unless the operator sets
`JobAggregation__Scheduler__Enabled=true`. It polls due sources at the configured
`JobAggregation__Scheduler__PollIntervalSeconds`; this source becomes due after
its `ScanIntervalMinutes` elapses. Existing in-process and PostgreSQL advisory
locks prevent duplicate runs across API instances. Automatic publication remains
controlled separately by `JobAggregation__AutoPublishEnabled` and its quality
gate; source jobs are otherwise created as Draft.

For an authenticated administrator, a local manual run is:

```powershell
$sourceId = '<job-source-guid>'
Invoke-RestMethod -Method Post -Uri "https://localhost:<api-port>/api/admin/job-sources/$sourceId/run" -Headers @{ Authorization = "Bearer <admin-access-token>" }
```

The run reads all advertised pages and details with bounded concurrency and
request spacing. It updates only jobs whose persisted source ID and external ID
match. Missing jobs are closed only after a complete snapshot and zero item
failures/skips. Manual jobs are never assigned to a source or reconciled by this
provider.
