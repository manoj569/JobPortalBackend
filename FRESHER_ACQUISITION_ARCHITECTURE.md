# Fresher Acquisition Backend

## Status

This document records the repository inspection and current backend foundation. The implementation now has separate acquisition identity and durable run/item records, but remains **not ready for staging** until bounded transactional processing and acquisition persistence/concurrency tests are complete.

## Existing data model inspected

- `Job` already contains `ApplicationUrl` (the current Apply Now destination), `CanonicalApplicationUrlHash`, `JobSourceId`, `ExternalJobId`, `FingerprintHash`, `FirstSeenAtUtc`, `LastSeenAtUtc`, `SourcePostedAtUtc`, `ExpiresAtUtc`, `Status`, `WorkplaceType`, and experience fields. `JobPortalDbContext` recomputes the application URL hash on save.
- PostgreSQL indexes already cover canonical URL hash, `(JobSourceId, ExternalJobId)` (unique when both are present), fingerprint, first/last seen, and active/status fields. The database creation lock coordinates URL/fingerprint writes.
- `Company` contains `Name`, `NormalizedName`, `Slug`, `WebsiteUrl`, and `LogoUrl`. The normalized name is unique among active companies. Its required `OwnerUserId` means automatic company creation cannot safely infer ownership.
- `Category` is a hierarchical table with stable slugs and parent/child links. Existing job ingestion can retain a null category; no uncontrolled category creation is appropriate.
- `JobSource` is tied to one `CompanyId`, has ATS type and career-page identity, and owns the existing source-level `AutoPublishEnabled` policy. `JobAutoPublishService` requires a job `JobSourceId` and checks that source/company ownership matches.
- `JobIngestionService` already performs canonical URL/fingerprint dedupe, company lookup by existing name/slug, item persistence, and India eligibility before create/update. It does not persist provider external identity unless `JobSourceId` is supplied.
- Internal authentication did not exist. Existing public authentication is JWT-based and not appropriate for an independent acquisition worker.

## Implemented first slice

`POST /api/internal/v1/jobs/bulk-import` is guarded by `FresherAcquisition:ServiceToken`, compared in constant time, and fails closed with 503 when unset or shorter than 32 bytes. Missing/invalid credentials return 401. It is rate limited to 30 requests/minute and applies configurable request byte and item limits (`FresherAcquisition:MaximumRequestBytes`, default 3,000,000; `FresherAcquisition:MaximumBatchSize`, default 500 and capped at 1,000).

Each item is classified independently. Geography and experience validation run before `IJobIngestionService`; rejection never calls the persistence path. A safe Apply URL is selected in this order: `applyUrl`, `canonicalJobUrl`, `discoveredUrl`. Only absolute HTTP/HTTPS DNS-host URLs are accepted; credentials, fragments, loopback, private IP literals, localhost/local/internal hostnames, and unsafe schemes are rejected. Query parameters are retained to avoid damaging ATS application links. Arbitrary `sourceMetadata` is intentionally ignored; only a small backend-generated eligibility/source summary is written to the existing metadata field.

`FresherAcquisitionEligibilityPolicy` is separate from `IndiaJobEligibilityPolicy` and is not referenced by Greenhouse, Lever, Ashby, Workday, or SuccessFactors provider execution. It recognizes zero-start experience ranges regardless of maximum, rejects positive mandatory minima before considering junior/fresher titles, treats senior titles as rejection signals, and keeps ambiguous geography/experience out of `Jobs`.

Existing `IJobIngestionService` provides canonical URL and fallback fingerprint dedupe under the repository's existing lock and persistence boundary. Thus retries with the same working URL do not create duplicate user-facing jobs. The current slice does not durably key `externalJobId` because doing so safely requires acquisition-source identity storage.

The current slice is intentionally not being represented as the finished bulk pipeline: `CompanyWebsiteUrl`, `CompanyLogoUrl`, `Skills`, arbitrary category values, and distinct discovery/canonical/apply URLs are accepted in the wire DTO but are not fully persisted; only the selected working URL is stored in `Job.ApplicationUrl`. Existing ingestion persists item-by-item. No durable import audit, replay receipt, lifecycle verification, or acquisition AutoPublish invocation exists yet. These require the schema and acquisition-source design below, rather than silently discarding their significance or overloading employer ATS source ownership.

## Required production architecture before staging

1. Add an `AcquisitionSource` entity independent of `JobSource`, keyed by stable source type/name, with enabled/AutoPublish policy and lifecycle settings. The `JobSource` table must remain employer-ATS-only.
2. Add acquisition identity to `Job` (source FK + external job ID) and a partial unique PostgreSQL index on active `(AcquisitionSourceId, ExternalJobId)`. Keep existing canonical URL hash and fingerprint indexes as cross-source dedupe. Generate a migration in `JobPortal.Persistence.Postgres` using the repository's EF migration convention.
3. Add durable `AcquisitionImportRun` and small per-item outcome/audit rows (no full descriptions), with unique import idempotency, counts, correlation ID, timestamps, and retention. A replay must return stored outcomes rather than repeat writes. Add indexes for source/time and source/external ID; do not store rejected descriptions.
4. Extend the application ingestion abstraction to accept acquisition identity and preserve guarded merge precedence. Acquire the existing URL/fingerprint lock plus source/external-ID identity lock; enforce database uniqueness for concurrent writers. Company resolution should prefer normalized website domain, then stable identifier, then normalized name. Creating a new company requires an explicit configured system owner/admin identity, not an invented `OwnerUserId`.
5. Resolve only existing categories through a bounded preloaded category map. Persist an uncategorized job when the current domain permits it; never create source categories ad hoc.
6. Adapt the existing `JobAutoPublishService` to accept an `AcquisitionSource` as an authority while leaving all `JobSource` ATS checks unchanged. The backend eligibility policy must run before persistence; publication remains downstream and cannot decide eligibility.
7. Add provider/canonical URL normalization for known tracking parameters only, preserving the working Apply URL separately when identity normalization changes it. Verify the existing redirect uses `ApplicationUrl` (it is the only current URL field used by the job model).
8. Add a lifecycle verifier using `LastSeenAtUtc`, source confirmation, `ExpiresAtUtc`, and a configurable stale threshold. Never expire solely from `CreatedAtUtc` age.

## Contract and policy

Request DTOs are in `JobPortal.Application/Features/FresherAcquisition/FresherAcquisitionContracts.cs`. The current endpoint accepts import/source identity plus a bounded list of candidate jobs. Its shape is:

```json
{
  "importId": "producer-generated-batch-id",
  "sourceType": "public-feed",
  "sourceName": "feed-name",
  "jobs": [{
    "externalJobId": "optional-provider-id",
    "companyName": "Example Employer",
    "companyWebsiteUrl": "https://example.com",
    "title": "Graduate Trainee",
    "description": "Role details...",
    "locationText": "Pune, India",
    "city": "Pune",
    "state": "Maharashtra",
    "country": "India",
    "workplaceType": "On-site",
    "remoteEligibility": null,
    "experienceText": "0-3 years",
    "minimumExperienceYears": 0,
    "maximumExperienceYears": 3,
    "employmentType": "Full-time",
    "category": "Engineering",
    "skills": ["C#", "PostgreSQL"],
    "discoveredUrl": "https://jobs.example.com/listing/123",
    "canonicalJobUrl": "https://careers.example.com/jobs/123",
    "applyUrl": "https://careers.example.com/apply/123",
    "postedAt": "2026-10-14T00:00:00Z",
    "expiresAt": null,
    "sourceLastSeenAt": "2026-10-15T00:00:00Z",
    "sourceMetadata": {"ignored": "untrusted metadata is not persisted"}
  }]
}
```

Send `Authorization: Bearer <service-token>`. The response contains import ID, aggregate counts, and one indexed outcome/reason code per input item. Outcomes include `Created`, `Updated`, `Existing`, `Duplicate`, `RejectedGeography`, `RejectedExperience`, `RejectedInvalidUrl`, `RejectedInvalidData`, and `Failed`. The producer must treat this endpoint as service-to-service only; the token belongs in deployment secrets/environment, never source control or request logs. `importId` is a durable per-source idempotency key whose candidate set is protected by a stable request fingerprint.

Experience policy accepts explicit fresher/intern/graduate/campus/apprentice signals and a numeric minimum of zero, including `0-2`, `0-3`, `0-5`, and `0 to 2`. A positive mandatory minimum wins over a fresher title. Senior/lead/staff/principal/manager/architect/director titles are rejected. Conflicting preferred/non-mandatory language is only exempted when the deterministic text rule can identify it; ambiguous evidence is not eligible.

Geography accepts evidenced India locations and unambiguously worldwide/global remote roles. A country-only or geographically ambiguous remote label is not enough. Explicit country residence restrictions win unless there is independently consistent India location evidence. Geography outcomes and reason codes are deterministic.

## Deployment and rollback

Configure `FresherAcquisition__ServiceToken` as a random secret of at least 32 bytes, `FresherAcquisition__MaximumBatchSize` (1–1,000), `FresherAcquisition__MaximumRequestBytes` (64,000–10,000,000), and `FresherAcquisition__ChunkSize` (1–200, default 50). The endpoint fails closed when the token is absent. Apply the additive PostgreSQL migrations before enabling the acquisition producer. Rollback is to disable producer traffic and remove the service-token environment variable; do not delete acquired `Jobs` or roll back their data automatically.

## Verification

Policy regression tests were added in `JobPortal.Application.Tests/FresherAcquisitionEligibilityTests.cs` for zero-minimum ranges, mandatory positive experience, explicit fresher signals, senior/conflicting titles, India/global/unknown/restricted geographies, unsafe URLs, and the no-persistence rule for rejected candidates. The Release API/application solution projects compiled successfully with `dotnet build JobPortal.sln -c Release --no-restore -m:1 -p:MSBuildEnableWorkloadResolver=false`; the solution build then failed while compiling the existing test project because repository-wide analyzers report numerous diagnostics in pre-existing test files. The test project compiled with analyzers disabled, but the VSTest host stalled after discovery and had to be stopped, so no test pass is claimed. Run the focused test class and broad suite in a normal developer environment before review.

## Repository architecture verification (before persistence implementation)

The current production EF context is `JobPortal.Persistence.Context.JobPortalDbContext`, registered through `JobPortal.Persistence`; PostgreSQL migrations live in `JobPortal.Persistence.Postgres`. The domain entities derive from `BaseEntity` (`Id`, UTC create/update, soft-delete fields), and persistence uses assembly-scanned `IEntityTypeConfiguration<T>` mappings. `Job` currently stores only the actionable `ApplicationUrl`; the existing `CanonicalApplicationUrlHash` is derived on `SaveChanges`. Aggregation identity is currently `JobSourceId` + `ExternalJobId`, and the partial unique index is filtered to active, non-deleted rows. URL/fingerprint dedupe is handled by the existing ingestion service and PostgreSQL lock.

`Company` requires an owning user and has `WebsiteUrl`, `LogoUrl`, and normalized-name uniqueness; no safe service-owned company-creation owner is configured, so acquisition must resolve existing companies and reject unresolved names. `Category` supports nullable job category and existing category lookup. Skills use `Skill.NormalizedName` and the `JobSkill` join with proficiency constraints. `JobSource` remains company-bound and is the authority for existing ATS AutoPublish. `JobAutoPublishService` currently authorizes only a matching active `JobSource` and delegates actual publication to `IJobService.PublishAsync`; this is the publication boundary to extend for acquisition while preserving all ATS checks.

The bulk endpoint uses an internal bearer token, constant-time comparison, bounded request size/item count, and a named rate limit. The request already carries `ImportId`, source type/name and separate discovered/canonical/apply URLs. At the time of this inspection there is no acquisition source, durable request run, per-item receipt, acquisition external identity, or transactional chunk abstraction. Existing import persists through `IJobIngestionService` item by item. These verified findings are the baseline for the implementation below.

## Current implementation status (2026-10-15)

The backend now has a separate `AcquisitionSource` with no employer FK, a durable `AcquisitionImportRun`, and compact `AcquisitionImportItem` receipts that omit job descriptions. The `AddFresherAcquisitionFoundation` migration adds these tables, acquisition-only source identity on `Jobs`, separate discovered/canonical URLs, last verification time, partial uniqueness for `(AcquisitionSourceId, AcquisitionExternalId)`, unique `(source, batch)` request identity, and the seeded `serpapi-google-jobs` source (`IsEnabled=true`, `AutoPublishEnabled=false`). `AddFresherRunRejectionCounters` persists replayable rejection totals. `AddNormalizedCompanyWebsiteDomain` adds and backfills normalized company domains for deterministic exact-domain lookup.

The existing authenticated/rate-limited endpoint now resolves `SourceType` only against an enabled persisted provider key; the caller's `SourceName` and any request value cannot enable publication. `ImportId` is the durable batch key. Completed retries return the stored run and item outcomes. Source external IDs are written to acquisition fields only. The ingestion path checks acquisition identity before URL/fingerprint dedupe and takes the existing PostgreSQL creation lock using an acquisition identity component before its race recheck. Database uniqueness remains the final race barrier. Separate discovered and canonical vacancy URLs are retained on the Job; `ApplicationUrl` remains the working Apply Now URL. Receipts store all three safe URLs and lifecycle timestamps.

Company resolution is now: trusted internal `CompanyId`, exact normalized website domain when it uniquely identifies one existing company, then the existing exact normalized name/slug lookup. Company creation remains disabled because `Company.OwnerUserId` is required and there is no configured system owner. A supplied category ID must exist; a supplied category name must resolve exactly to an existing category; otherwise the item receives `CATEGORY_RESOLUTION_FAILED`. Supplied skills use existing `Skill.NormalizedName` and `JobSkill` relationships. No company or category is fabricated.

AutoPublish continues through the existing `JobAutoPublishService` and `IJobService.PublishAsync`. The service now accepts an enabled acquisition source as an independent authority and reads `AutoPublishEnabled` from that database row. Existing ATS checks remain in the `JobSource` branch. The source is seeded with AutoPublish off; an administrator must explicitly enable it in the persisted acquisition-source configuration. Publication exceptions are reported as `PUBLICATION_FAILED` while preserving the already persisted Job.

Fresher Acquisition now uses a separate no-save ingestion entry point and the acquisition store owns each short database transaction. The default chunk size is 50 and is configurable with `FresherAcquisition:ChunkSize` (clamped to 1–200); ATS callers continue using `IngestAsync`, which saves immediately as before. Each transaction takes PostgreSQL transaction-scoped advisory locks for normalized external identities in its chunk, invokes ingestion without `SaveChanges`, adds the corresponding receipts, then performs one `SaveChanges` and commits. A rollback clears the scoped EF change tracker. Rejected and invalid candidates also get durable receipts in the chunk; they never create Jobs.

### Identity, resume, accounting, and publication

`FresherCandidateIdentity.Create` hashes the acquisition source ID plus the first available normalized identity: external ID, canonical URL, apply URL, or normalized company/title/location. It excludes request index and time. The request fingerprint hashes the ordinal-sorted distinct candidate identity keys, so reordering and repeated copies of one identity do not change the logical batch. `AcquisitionImportItems.IdentityKey` remains the physical column and maps to the domain property `CandidateIdentityKey`; its existing unique index is `(run, candidate identity)`.

`AcquisitionImportRuns` has a database unique index on `(AcquisitionSourceId, BatchId)` (BatchId is the API ImportId) and now stores `RequestFingerprint`. The store handles only PostgreSQL SQLSTATE `23505` for the exact run unique-index name, reloads the surviving run, and compares fingerprints. A different fingerprint raises `IMPORT_ID_PAYLOAD_CONFLICT`, returned as HTTP 409. Same-ImportId requests also take a session-level PostgreSQL advisory lock for the full import; a competing active request gets HTTP 409 `IMPORT_IN_PROGRESS`. The lock is released when the request scope exits. Cross-ImportId external identity writes take transaction-scoped advisory locks and remain protected by the existing partial unique Job index; the advisory lock is held until the Job and receipt transaction commits.

On retry, the service loads receipts from the same run and skips already recorded candidate identities. It processes only missing identities in further bounded chunks. Same-request duplicate identities produce an indexed `Duplicate` response item but count once in durable totals. `ReceivedCount` is reconciled to the unique receipt count, not the number of HTTP attempts. Final Received, eligibility/rejection, Created, Updated, Existing, Failed and Published counters are recalculated from durable receipts; replayed requests return these stored totals while mapping receipt outcomes into the current request order. `Created` is the durable outcome recorded in the one transaction that committed the newly inserted Job and receipt.

Publication occurs only after the chunk transaction commits. A receipt records `Pending`, `Published`, `NotPublished`, or `Failed` publication state; exception type is retained as a bounded diagnostic, never a stack trace. A run with Pending/Failed publication remains `AwaitingPublication`, and a retry of the same ImportId retries publication using the receipt's existing JobId. No second Job is created for this retry. The existing acquisition source `AutoPublishEnabled` value is still authoritative.

The PostgreSQL test suite now includes commit/rollback atomicity, URL receipt round-trip, same-ImportId replay/fingerprint conflict, concurrent ImportId creation, external-ID uniqueness, reordered multi-chunk resume, duplicate request items, and publication-failure retry. It uses the established `GENERIC_INDIA_TEST_POSTGRES` disposable local database contract and was not executable in this environment because that variable was unset. Those PostgreSQL cases are therefore NOT EXECUTED here. To run them from PowerShell against the disposable local test database:

```powershell
$env:GENERIC_INDIA_TEST_POSTGRES = "Host=127.0.0.1;Database=careerharbor_generic_india_test;Username=<local-user>;Password=<local-password>"
dotnet test .\JobPortal.Application.Tests\JobPortal.Application.Tests.csproj --no-restore -p:RunAnalyzers=false --filter FullyQualifiedName~FresherAcquisitionPostgresTests
```

The endpoint requires `FresherAcquisition:ServiceToken` (at least 32 bytes) as before. Configure maximum body size and item count using `FresherAcquisition:MaximumRequestBytes` and `FresherAcquisition:MaximumBatchSize`; `SourceType` must equal an enabled persisted `ProviderKey`, initially `serpapi-google-jobs`. No Python worker, SerpApi fetcher, scheduler, crawler, frontend, or lifecycle deletion process is included. No migration was applied to any database.

Current verification for this hardening: the focused acquisition and ATS regression filter passed (85 passed); all seven PostgreSQL tests were skipped because `GENERIC_INDIA_TEST_POSTGRES` was unset. The strict Release API build passed with zero warnings/errors. Full Release solution build passed with analyzers disabled; the analyzer-enabled full solution build is blocked by 186 pre-existing test-project diagnostics, with no analyzer findings in the newly added acquisition tests or modified production projects. The EF migration snapshot reports no pending model changes. No migrations were applied to any database. No Python worker, SerpApi fetcher, scheduler, crawler, frontend change, deploy, or push is included.
