# Phase 3: job source administration and manual runs

All routes require the existing `Administrator` role and use the normal
`ApiResponse<T>` envelope. No source scanner, timer or scheduler is added.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/admin/job-sources` | Paged source list |
| GET | `/api/admin/job-sources/{id}` | Source details and resolved category |
| POST | `/api/admin/job-sources` | Create a source for an existing company |
| PUT | `/api/admin/job-sources/{id}` | Replace editable configuration |
| DELETE | `/api/admin/job-sources/{id}` | Deactivate and soft-delete |
| POST | `/api/admin/job-sources/{id}/run` | Await one manual run |

List parameters: `pageNumber` (default 1), `pageSize` (default 20, maximum 100),
optional `companyId`, `atsType`, and `isActive`. Results are ordered by creation
time descending, then ID, and exclude soft-deleted sources. Missing IDs return
404. Validation failures use the existing API validation response. Duplicate
configuration or a source currently in use returns 409.

## Editable configuration

Create/update accepts `companyId`, `careerPageUrl`, `atsType`, `atsIdentifier`,
`isActive` (default true), and `scanIntervalMinutes` (default 720).
Company must already exist. Career page URLs must be absolute HTTP/HTTPS,
credential-free, and at most 2048 characters. ATS identifiers are trimmed,
at most 255 characters, and required for Greenhouse/Lever. Scan intervals must
be 1–10080 minutes; the value is stored only and does not schedule execution.

- Greenhouse (`1`): `atsIdentifier` is the public board token.
- Lever (`2`): `atsIdentifier` is the public site/company token.
- Custom (`0`): may be stored, but a manual run reports no registered provider.

`careerPageUrl` remains human-facing and is never scraped. Providers use their
existing public ATS API clients. Creation/update checks the existing filtered
unique key `(CompanyId, AtsType, AtsIdentifier)`; uniqueness races are translated
to a safe conflict response, without database exception details.

## Operator-managed category mappings

The API does **not** accept or persist a category assignment. `categoryId` and
`categoryName` are response-only values resolved from application configuration.
There is no `JobSource.CategoryId` column and no configuration mutation endpoint.

After creating a source, take its returned ID and configure:

```text
JobAggregation__SourceCategories__<SOURCE_ID>=<EXISTING_CATEGORY_ID>
```

Use canonical hyphenated GUIDs for source keys. Configure the variable in the
deployment platform's environment settings, or in an external environment file;
do not put production IDs into tracked appsettings files. GUIDs contain hyphens,
so a POSIX shell's `export` identifier syntax is unsuitable; use platform
environment settings or `env 'KEY=VALUE' <application-command>` instead.
On Windows, deployment tooling or `Environment.SetEnvironmentVariable` can set
the full key. .NET's existing environment configuration provider translates
double underscores into configuration path separators.

Restart/redeploy the application after changing environment variables: the
environment provider does not reload automatically. Reloadable configuration
providers are observed through `IOptionsMonitor<JobAggregationOptions>`. No
production configuration is changed by this implementation.

The resolver safely parses the configured category GUID and verifies that it
exists and is not soft-deleted. Missing, malformed, empty or nonexistent category
IDs resolve to null. No category is guessed or automatically created.

The runner resolves once per run, enriches each `RawExternalJob.CategoryId`, and
calls the existing ingestion service. With no valid mapping, existing canonical
jobs can still match and refresh LastSeen; genuinely new jobs are Invalid and
counted as skipped. A valid mapping enables new Draft jobs subject to moderation.
The mapping never reclassifies an existing canonical job.

## Run responses and operational status

Runs return `JobSourceId`, `TotalReceived`, `Created`, `Matched`, `Skipped`,
`Failed`, `Succeeded`, and a sanitized `Error`. A completed fetch/process loop
can have `Succeeded=true` with skipped or failed individual records; inspect
the counters. Inactive/unsupported sources return `Succeeded=false`. Provider
failures are sanitized by the existing runner. Cancellation propagates.

GET responses include company identity, editable source fields, resolved category,
`LastRunAtUtc`, `LastSuccessfulRunAtUtc`, `LastError`, `ConsecutiveFailures`, and
creation/update timestamps. Error display uses a fixed safe message. A successful
run clears failure state; provider failure retains the last successful timestamp.
No historical run table is added.

Create/update/delete use the existing audit writer. Runs persist a requested
audit event before execution and a succeeded/failed event after a returned
result. Cancellation or an unhandled infrastructure failure can leave only the
requested event. External descriptions and exception details are not audited.

## Safety and limits

A singleton in-memory guard rejects overlapping manual runs or edits/deletes
for one source in this process, releasing on success, failure or cancellation.
It is not a distributed lock. Multiple API instances, or separate sources that
return the same new job, can still race. Phase 2's concurrent-create risk remains:
the fingerprint index is non-unique. The existing PostgreSQL source index also
does not enforce uniqueness for null Custom identifiers under concurrent creation.

Canonical jobs retain company/category, curated fields, publication/approval state,
referrals and recruiter contacts. Phase 1 dedup and Phase 2 ingestion rules remain
authoritative. Adzuna discovery/scheduling and AI Apply adapters are unchanged.
Phase 3 makes **zero entity, schema, index or migration changes** and performs no
deployment or database update.
