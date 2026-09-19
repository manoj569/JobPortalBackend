# Phase 6: aggregation production hardening

## Scope and frozen behavior

The shared manual/scheduled runner, provider endpoints, category precedence,
Draft-only external creation and URL -> fingerprint -> fuzzy dedup order remain.
Fingerprint hashing, fuzzy threshold 0.85 and company-scoped 100-candidate limit
are unchanged. Adzuna discovery and AI Apply adapters remain separate.

**Referral architecture is frozen.** JobReferral's one-to-one/unique JobId,
ReferralUnlock, approval/API contracts and recruiter relationships are unchanged.
Multiple-referrer support and broader Admin/Referral canonical-job unification
are deferred. Aggregation never merges existing rows or moves relationships.
Matches only refresh LastSeen and fill missing FirstSeen/fingerprint; the new URL
hash is derived lookup metadata, not replacement of the actionable URL.

## 6A: cross-instance source coordination

Scheduler/manual run/update/delete acquire the existing process-local guard, then
`IJobSourceExecutionLock`, then load/recheck the source. The runner does **not**
reacquire this lock. Nested async disposal releases distributed before local.
Scheduler contention logs and skips; manual/admin contention returns the existing
`ConflictException` code `job_source_busy`. Neither changes failure counters,
timestamps, source state, or emits run audit rows for a run that never started.

The existing `IJobSourceExecutionLock` contract is preserved. The user's initial
Postgres implementation retains its source key domain and delegates session
lifecycle to a helper shared with creation locking. Key derivation is SHA-256 of
`CareerHarbor:JobSourceExecution:v1:<guid-D>`, first eight bytes as signed big-endian
Int64, without modulo/random/runtime hash codes.

Each lease owns a **separate, pinned Npgsql connection**, never EF's connection.
Source acquisition executes `pg_try_advisory_lock`; false closes the connection.
Successful leases keep it open and explicitly unlock on that same session.
Dispose is idempotent. Commands are parameterized; acquire timeout is 30 seconds,
cleanup timeout five seconds per key. Caller cancellation cannot cancel cleanup.
Acquisition errors and cleanup errors still dispose the connection.

Lock connections disable Npgsql pooling and multiplexing. Physical close is the
fallback release when acquisition outcome is ambiguous or explicit unlock fails;
no possibly locked session is returned to an Npgsql pool. This trades connection
setup overhead for clear ownership. EF keeps its existing pooling/retry settings.
No transaction spans ATS HTTP. A running source can use a source connection, a
short-lived creation connection and EF connection concurrently: budget database
connection capacity accordingly.

**Endpoint prerequisite:** `DefaultConnection` must reach direct PostgreSQL, or a
correctly configured session-mode proxy. Transaction/statement pooling is not
compatible with session advisory locks. Known Neon `-pooler.` hosts are rejected
without printing the endpoint/credentials. Arbitrary proxy mode cannot be detected
from a connection string; operators must verify it. No configuration files were
changed. Losing a server session releases its locks; leases are cooperative locks,
not fencing tokens against work continuing after a network partition. Full fencing
and failover fault-injection validation remain future work.

References: [PostgreSQL advisory locks](https://www.postgresql.org/docs/18/functions-admin.html),
[Npgsql connection settings](https://www.npgsql.org/doc/connection-string-parameters).

## 6B: concurrent creation

Ingestion first deduplicates normally. A match follows the existing safe-update
path with no creation lock. A potential create acquires `IExternalJobCreationLock`,
reruns the complete dedup sequence, then returns the newly observed match or saves
one Draft under the lease. Provider fetch is already complete before acquisition.

URL and fingerprint keys have separate versioned domains:

- `CareerHarbor:JobCreation:Url:v1:<canonical-url>` when URL is present.
- `CareerHarbor:JobCreation:Fingerprint:v1:<fingerprint>` always.

Both are used when available: URL coordinates changed metadata; fingerprint
coordinates different URLs for the same existing fingerprint identity. Keys are
deduplicated and numerically sorted to avoid deadlocks. Blocking advisory acquisition
is caller-cancellable and command-timeout bounded. Different identities share no
global lock. No unique fingerprint constraint was added. Hash collisions only
serialize unrelated work; they cannot by themselves cause a dedup match.

Limits: all participating aggregation instances must run this implementation.
Admin/referral or external SQL writers do not acquire these creation locks.
Different URLs **and** different fingerprints that only fuzzy-match do not share
a lock; serializing an entire company would be a different throughput tradeoff.
Existing duplicates are not repaired. This is stable-identity race protection,
not a global uniqueness guarantee or exactly-once ingestion.

## 6C: canonical URL lookup and approved migration

Previously the repository compared canonical input to raw `ApplicationUrl`, missing
tracking/query/slash variants. Bounded candidate scanning cannot guarantee exact
matches; runtime normalization over every stored row would be an unindexed scan.
The user explicitly approved schema support and documentation of the backfill.

New PostgreSQL migration:
`20260919222024_AddCanonicalJobApplicationUrlHash`

Its Up adds **only** nullable `Jobs.CanonicalApplicationUrlHash` varchar(64) and
`IX_Jobs_CanonicalApplicationUrlHash`, a non-unique filtered index over active,
nonnull hashes. There are no FK/referral/salary/xmin/JobId1 changes or drops in Up.
Only its new migration/Designer and current PostgreSQL snapshot change. Historical
and SQL Server migrations remain untouched. This migration was generated offline,
**not applied**, and contains no production backfill SQL.

`ApplicationUrlIdentity.Hash` computes lowercase SHA-256 of the canonical HTTP(S)
URL, excluding invalid/non-HTTP/credential-bearing URLs. DbContext derives the hash
on added/modified Job saves, covering existing writers without changing their
contracts. Original actionable `ApplicationUrl` is preserved byte-for-byte.
Repository URL lookup is a parameterized indexed equality predicate, not a table
scan or a capped heuristic. Invalid keys return no URL match. Direct SQL/bulk writers
that bypass SaveChanges must also maintain the derived key.

Narrow canonicalizer corrections: remove the unbounded static cache; normalize
non-root trailing slashes; preserve repeated/case-sensitive unknown query names and
bare flags rather than dictionary last-value-wins collapse. Tracking removal is
still limited to the existing eight names, case-insensitively. Scheme/host/default
ports/fragments/path case and deterministic ordering are retained. Duplicate-value
ordering is preserved because its semantics are unknown. Malformed/relative input
retains the old safe canonicalizer fallback, but cannot become an indexed HTTP key.

### Required deployment/backfill procedure (NOT executed)

This is **not ready for direct rolling deployment over unbackfilled legacy rows**.
Null keys from the migration will not magically match canonical URL variants.
Fingerprint/fuzzy fallback is not a substitute for completing the backfill.

1. Drain/pause aggregation and manual runs on every instance. Plan migration/index
   lock duration against production table size; generated CreateIndex is not
   concurrent. Coordinate schema/application rollout before resuming traffic.
2. Apply the reviewed new PostgreSQL migration through the separately approved
   deployment process. Do not run this branch against the old schema.
3. Deploy code with URL-key maintenance to all Job writers. Do not run mixed old
   and new aggregation instances; old instances do not coordinate advisory locks.
4. Implement/review a one-off bounded backfill using this exact compiled
   `ApplicationUrlIdentity.Hash` implementation. Read active rows in keyset pages
   (e.g. 500 IDs/URLs at a time), never load the entire table into application memory.
   Compute keys in .NET, not a subtly different SQL URL parser. Update only the
   hash with a compare-and-set predicate: Id matches, ApplicationUrl still equals
   the value read, and old hash still equals the value read. Use parameterized
   ExecuteUpdate/SQL so other fields, timestamps, referrals and audits are unchanged.
   Skip invalid/non-HTTP URLs, record their count, and retry rows concurrently edited.
   Persist progress externally; make the job restartable/idempotent. This operational
   backfill executable has **not** been added or run by this task.
5. Verify every eligible active HTTP(S) row has the expected key; verify sample
   tracking/query/default-port/slash variants and preservation of raw URLs. Check
   the index query plan on a nonproduction copy. Review existing duplicates; do not
   auto-merge them, especially jobs with referrals/applications.
6. Verify a direct/session-pinned endpoint and multi-instance advisory-lock behavior
   on an isolated PostgreSQL database. Resume aggregation only after sign-off.

## 6D: provider resilience and rate limits

Only the three public ATS named HttpClients receive `AggregationHttpRetryHandler`.
GET attempts are cloned; requests/responses from retries are disposed. Three total
attempts maximum, ten seconds per attempt (including response buffering), with
one/two-second backoff. Retryable statuses: 429, 500, 502, 503, 504. Network exceptions
and non-caller timeout cancellation retry within the same bound; permanent 4xx/501
do not. Caller cancellation stops attempts/delays immediately. No new packages.

Valid Retry-After delta/date controls delay. Negative/past delay is immediate.
A delay over 30 seconds returns the failure rather than retrying early or keeping
the source running indefinitely. A singleton, process-local cooldown remembers
429 Retry-After per known provider, so subsequent calls return a local 429 without
another network request until the cooldown expires. It does not disable sources.
The per-provider cooldown is conservative (may defer other boards on that host),
not a distributed/global rate limiter; separate instances/restarts retain no shared
cooldown. Normal scan intervals remain unchanged. No immediate scheduler retry loop.

## 6E: audit, logging and failure tracking

Create/update/delete audits are preserved. Manual execution retains request/result
audits; results now include received/created/matched/skipped/failed counts, not raw
provider errors. No per-fetched-job audit row. As before, a process crash or caller
cancellation can leave a request audit without a result; no fabricated completion.

Structured scheduler logs retain start/completion/failure and add distributed
contention and received count. Provider retry/rate-limit logs use fixed provider
labels, attempt/status/delay only. No request URLs, headers, bodies, exception
messages or connection strings are passed to these logs. Failed source runs still
increment ConsecutiveFailures, success resets it, contention is not a failure, and
sources are never automatically disabled. Cleanup/connectivity failures propagate
through existing sanitized failure handling; they are not reported as contention.

## Tests and operational limitations

New focused suites cover deterministic/domain-separated keys, session lifetime,
busy/cancellation/unlock failure cleanup, shared-lock scheduler contention, manual
run/update/delete conflicts, race/recheck paths, independent identities, curated
referral/recruiter preservation, URL variants/index metadata, and bounded HTTP
retry/Retry-After/cancellation/timeout/cooldown behavior. Existing fixture/DI tests
are updated for required lock dependencies; no production no-op lock is registered.

Advisory SQL lifecycle is tested using fake DbConnections and orchestration using
handwritten lock fakes. EF query/model and migration inspection are offline. This
does **not** claim live PostgreSQL/Neon lock validation, failover fencing, or a live
backfill. The three existing live PostgreSQL tests remain excluded. These need an
explicitly isolated test database before operational rollout.

All changes remain unstaged for manual review. No commits/pushes/merges, stash
operations, database updates or production data changes were performed. The known
SQL artifact and nested duplicate directory are untouched.

## Executed validation

All three builds below succeeded with zero warnings and errors:

```powershell
dotnet build JobPortal.Persistence.Postgres/JobPortal.Persistence.Postgres.csproj --no-restore
dotnet build JobPortal.API/JobPortal.API.csproj --no-restore
dotnet build JobPortal.sln --no-restore
```

Focused Phase 6: 43 passed, 0 failed, 0 skipped.

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~JobAggregationHardeningTests|FullyQualifiedName~JobAggregationCreationRaceTests|FullyQualifiedName~JobAggregationHttpResilienceTests"
```

Phase 1–5 and referral/lifecycle regression selection: 253 passed, 0 failed,
0 skipped. These are overlapping subsets of the full suite, not additive totals.

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~JobDeduplicationServiceTests|FullyQualifiedName~JobFingerprintServiceTests|FullyQualifiedName~UrlCanonicalizerTests|FullyQualifiedName~JobIngestionServiceTests|FullyQualifiedName~JobSourceRunnerTests|FullyQualifiedName~ExternalJobProviderTests|FullyQualifiedName~JobAggregationDependencyInjectionTests|FullyQualifiedName~JobReferralServiceTests|FullyQualifiedName~AdminJobLifecycle|FullyQualifiedName~AutomaticJobExpiry|FullyQualifiedName~AdminManagementTests|FullyQualifiedName~JobSourceCategoryResolverTests|FullyQualifiedName~JobSourceAdministrationTests|FullyQualifiedName~JobSourceManualRunTests|FullyQualifiedName~JobSourceAuthorizationTests|FullyQualifiedName~JobAggregationDueSourceTests|FullyQualifiedName~JobAggregationScheduler|FullyQualifiedName~ExternalJobNormalization|FullyQualifiedName~ExternalJobCategoryMapping|FullyQualifiedName~ExternalJobCoverage" --logger "console;verbosity=minimal"
```

Full non-database selection: **866 passed, 0 failed, 0 skipped, total 866**.
The filter excludes the three live PostgreSQL tests; they are not counted as skipped.
Existing browser tests use local fixtures, not ATS/Neon production services.

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName!~AIApplyDistributedPostgresTests" --logger "console;verbosity=minimal"
```

Earlier targeted compilation used the permitted test-only analyzer workaround
`"-p:NoWarn=CA1707%3BCA1859%3BCA1861"`. Global warning policy was not changed.

Migration generation and model validation used a child-shell-only dummy connection
string, not configured credentials. Neither command opens a database connection:

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Database=phase6_model_only;Username=postgres;SSL Mode=Disable"
dotnet ef migrations add AddCanonicalJobApplicationUrlHash --project JobPortal.Persistence.Postgres --startup-project JobPortal.API --context JobPortalDbContext --no-build
dotnet ef migrations has-pending-model-changes --project JobPortal.Persistence.Postgres --startup-project JobPortal.API --context JobPortalDbContext --no-build
```

Generation succeeded; pending-model check reported no changes since the migration.
Designer and snapshot model bodies match. `git diff --check` passed.

## Change inventory

New files created in this task:

- `JobPortal.Application/Abstractions/Jobs/ApplicationUrlIdentity.cs`
- `JobPortal.Application/Abstractions/Jobs/IExternalJobCreationLock.cs`
- `JobPortal.Infrastructure/Services/AggregationHttpRetryHandler.cs`
- `JobPortal.Persistence.Postgres/PostgresAggregationLocks.cs`
- `JobPortal.Persistence.Postgres/Properties/AssemblyInfo.cs`
- `JobPortal.Persistence.Postgres/Migrations/20260919222024_AddCanonicalJobApplicationUrlHash.cs`
- `JobPortal.Persistence.Postgres/Migrations/20260919222024_AddCanonicalJobApplicationUrlHash.Designer.cs`
- `JobPortal.Application.Tests/JobAggregationCreationRaceTests.cs`
- `JobPortal.Application.Tests/JobAggregationHardeningTests.cs`
- `JobPortal.Application.Tests/JobAggregationHttpResilienceTests.cs`
- `JobPortal.Application.Tests/TestAggregationLocks.cs`
- `docs/job-aggregation-phase6.md`

Existing files modified:

- `JobPortal.API/Program.cs`
- `JobPortal.API/Services/JobAggregationScheduler.cs`
- `JobPortal.Application/Abstractions/Jobs/UrlCanonicalizer.cs`
- `JobPortal.Application/Features/JobAggregation/JobSourceManagementService.cs`
- `JobPortal.Application/Services/JobIngestionService.cs`
- `JobPortal.Domain/Entities/Job.cs`
- `JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.Persistence/Configurations/EntityConfigurations.cs`
- `JobPortal.Persistence/Context/JobPortalDbContext.cs`
- `JobPortal.Persistence/Repositories/JobRepository.cs`
- `JobPortal.Persistence.Postgres/PostgresJobSourceExecutionLock.cs` (already untracked before this task)
- `JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs`
- `JobPortal.Application.Tests/JobAggregationDependencyInjectionTests.cs`
- `JobPortal.Application.Tests/JobAggregationSchedulingTests.cs`
- `JobPortal.Application.Tests/JobIngestionServiceTests.cs`
- `JobPortal.Application.Tests/JobSourceAdministrationTests.cs`

The pre-existing untracked
`JobPortal.Application/Abstractions/Jobs/IJobSourceExecutionLock.cs` is preserved
unchanged. The unrelated SQL artifact is also preserved unchanged and untracked.
