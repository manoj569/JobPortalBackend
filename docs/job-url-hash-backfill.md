# Job URL hash backfill

Phase 6 added nullable `Jobs.CanonicalApplicationUrlHash`. Legacy rows need an
explicit backfill before indexed URL matching can reliably find them. This CLI
uses the existing `ApplicationUrlIdentity.Hash` without duplicating its logic.
No migration is required. This document is an operator procedure, not evidence
that the command has been run against production.

## Commands

### Advisory-lock verification before scheduler activation

```powershell
dotnet run --project JobPortal.Maintenance -- job-aggregation-lock-test
```

Uses `ConnectionStrings__DefaultConnection` and the exact production
`PostgresAdvisorySession` key derivation, connection guard, parameterized acquisition
and lease cleanup implementation. Production runtime behavior is unchanged; the
maintenance assembly has friend access to the internal helper. No copied lock SQL
or approximation. Each run uses a fresh GUID under a dedicated
`CareerHarbor:Maintenance:AdvisoryVerification:v1:` identity domain, never real
JobSource IDs or creation identities. All identities ultimately share PostgreSQL's
64-bit lock space, so a theoretical hash collision cannot be absolutely excluded;
every acquisition is nonblocking and no real lock is unlocked by another session.

Opens dedicated unpooled/nonmultiplexed connections. Checks acquisition, same-key
exclusion, concurrent different keys, normal lease release/reacquisition, physical
connection disposal without prior explicit unlock, and pre-cancelled acquisition
cleanup. All leases/connections are disposed on success or failure. It executes
only the production SELECT advisory lock/unlock calls: temporary session lock
state changes, **no business/table writes, DDL, migrations, providers or scheduler**.
This is not a pure observer: it intentionally acquires isolated ephemeral locks.

Expected output includes PASS for First acquisition, Same-lock exclusion,
Release/reacquisition, Different-lock concurrency, Connection-disposal release,
Pre-cancelled acquisition cleanup, Direct/session endpoint, and Overall
advisory-lock verification. Any failed check/errors return 1; missing configuration
returns 2; cancellation or the 90-second overall deadline returns 130. Existing
production acquire/cleanup timeouts also apply. No raw exceptions or secrets print.

Known Neon `-pooler.` hosts are rejected by the production guard. A successful
run verifies observed session semantics, not an arbitrary proxy's configuration;
operators must supply and confirm the direct Neon endpoint. Pre-cancellation is
tested, not every possible mid-command network failure/failover. The command was
not run against production during implementation. Run it manually before enabling
the scheduler, after backfill verification; it never enables the scheduler itself.

### Read-only post-backfill verification

```powershell
dotnet run --project JobPortal.Maintenance -- job-url-hash-backfill --verify
```

`--verify` is exclusive: it cannot combine with apply, trigger inspection or batch
options. It never instantiates the backfill store. It uses a REPEATABLE READ
transaction with `SET TRANSACTION READ ONLY` before any data SELECT, then streams
Jobs and performs aggregate SELECTs in the same snapshot. No data writes or DDL.
Only counts, numeric Status codes and PASS/FAIL are output, never URLs/hashes or
raw exception messages. Read permissions and complete visibility of all four
tables are required. Missing tables/permissions fail the command; no partial
report is accepted under row-level security: `SET LOCAL row_security = off` makes
policy-filtered access fail instead of silently verifying a subset (it grants no
bypass permission). No partial
report is presented as a successful verification. Queries use the connection's
search path, just like the backfill.

Reports all/active/deleted counts, populated and active NULL hashes, exact shared
helper eligibility and mismatches, duplicate active hash groups, Status counts,
Jobs with referrals/contacts/applications and orphan rows. Populated mismatch
checks include soft-deleted Jobs. Relationship counts include all physical rows
(including soft-deleted ones); an orphan has no matching physical Jobs row.
Jobs-with-relationship counts count each Job once per relationship type.
Duplicate count is distinct non-NULL hash values shared by two or more active Jobs,
not excess rows. With no approved duplicate allowlist, every duplicate group fails
the duplicate check and requires manual review; nothing is merged or repaired.

All four checks must pass for exit 0. Failed checks/incomplete queries return 1;
missing configuration returns 2, cancellation 130. Invalid active NULL URLs alone
are permitted. Long-running snapshots can hold back vacuum: schedule the full
scan appropriately (120-second command timeout), cancel if needed, and rerun.
This command was implemented and unit-tested only, not run against production.

Code review confirms `PostgresBackfillStore.UpdateSql` has exactly one SET target:
`CanonicalApplicationUrlHash`. ApplicationUrl is only a concurrency predicate.
It does not update UpdatedAtUtc, Status, publication fields, referrals, or other
business fields, and never calls SaveChanges. External database triggers remain a
separate consideration; use trigger inspection before applying.

### Trigger inspection and backfill

Before backfill, inspect user-defined triggers with:

```powershell
dotnet run --project JobPortal.Maintenance -- job-url-hash-backfill --check-triggers
```

This exclusive option cannot be combined with `--apply` or batching options. It
executes only catalog SELECTs, resolves `"Jobs"` using the same search path as the
backfill, excludes internal triggers, and reports name, timing/events, enabled
state and definition. Definition string literals are redacted because trigger
arguments can contain secrets; function bodies are not retrieved. No triggers is
reported explicitly; a missing table or query failure returns nonzero, not a false
clean result. Review trigger function effects separately through secure DBA tools.
This command never runs the backfill or starts the scheduler.

Supply `ConnectionStrings__DefaultConnection` securely through the process
environment (for example through an approved secret manager). No appsettings are
read. Do not paste credentials into shell commands/history or tickets. Use a direct
PostgreSQL endpoint; known Neon `-pooler.` endpoints are rejected. Keep TLS enabled
with your approved connection settings. Use a least-privilege maintenance identity.

```powershell
dotnet run --project JobPortal.Maintenance -- --help
dotnet run --project JobPortal.Maintenance -- job-url-hash-backfill
dotnet run --project JobPortal.Maintenance -- job-url-hash-backfill --batch-size 500
# Only after separately approving the target database and reviewing dry-run counts:
dotnet run --project JobPortal.Maintenance -- job-url-hash-backfill --apply --batch-size 500
# Clear the secret from this PowerShell process afterward:
Remove-Item Env:ConnectionStrings__DefaultConnection
```

Unknown/repeated options fail. Batch size is 1–5000, default 500. No arguments do
not connect: a command is required. Without `--apply`, the backfill command has no
write path and additionally sets the session's default transactions read-only.
No credentials, URLs, connection strings or raw exception messages are printed.
Ctrl+C cancels; exit codes are 0 success, 1 failure, 2 usage/configuration error,
130 cancellation. Do not treat a partial summary from a nonzero exit as success.

## Scope and safety

Only non-deleted Jobs with a NULL hash are read in deterministic Id keyset pages.
Soft-deleted Jobs remain untouched, consistent with the active-row filtered index.
Invalid/empty/non-HTTP/credential-bearing URLs stay NULL according to the shared
hash helper. Existing non-NULL values (including incorrect values) are not repaired.

Each eligible batch uses one transaction. Each parameterized update sets only
`CanonicalApplicationUrlHash` and requires Id, still-NULL hash, still-active state,
and the exact ApplicationUrl read. Concurrent URL edits, deletion or hash population
skip the update. Counts use affected rows only after successful commit. A failed
batch rolls back and stops with nonzero exit; earlier committed batches remain.
An ambiguous connection failure during commit may mean that batch committed even
though it was not included in the summary. Rerunning is safe and reconciles this.

No tracked entities or SaveChanges are used. ApplicationUrl, UpdatedAtUtc, all
business fields and relationships remain unchanged by the SQL. Verify production
has no custom UPDATE triggers with side effects before applying; the utility cannot
prevent externally installed triggers from running. It creates no triggers/schema.

Reads are bounded in memory, not a whole-table snapshot. Invalid rows are passed
once per run, so they cannot cause an infinite loop. Concurrent inserts/changes
behind the keyset cursor may need a rerun. Run during a controlled maintenance
window; pause other Job writers if exact stable reconciliation counts are needed.
Counts scan the table but retrieve only four aggregate values. Commands retain
Npgsql's finite command timeout; a timeout fails safely rather than retrying writes.

## Reports and verification

Initial counts: total Jobs, NULL hashes across all Jobs, already populated hashes,
active NULL hashes targeted, and total not targeted. Processing counts: scanned,
eligible URLs, would update (dry-run only), committed updates, invalid/empty skipped,
concurrent changes skipped, completed batches, failures. Soft-deleted NULL hashes
are included in initial all-Job counts but excluded from scanned/eligible counts.
Counts taken at different times can differ when concurrent writers are active.

After apply, rerun dry-run: eligible/would-update should be zero for a stable table;
remaining invalid URLs and deleted rows may legitimately have NULL hashes. Investigate
concurrency skips and rerun if necessary. Compare pre/post business-field samples
and timestamps using approved read-only checks; verify representative canonical URL
lookups. No automatic merging or duplicate repair is performed.

**Keep the aggregation scheduler disabled until backfill AND isolated PostgreSQL
advisory-lock verification are complete.** This utility never starts the API or
scheduler. Referral schema/cardinality and Adzuna/AI Apply remain unchanged.

Tests use a handwritten store and inspect the exact production UPDATE statement.
They cover orchestration, unchanged business values, CAS guards, batching,
idempotency, cancellation and failure reporting without connecting to Neon.
They do not claim live PostgreSQL transaction/trigger verification.

## Implementation validation

`dotnet restore JobPortal.sln --ignore-failed-sources` succeeded after granting
access to the existing user NuGet configuration. The requested plain
`dotnet build JobPortal.sln --no-restore` hit 80 existing test analyzer errors.
Using the established command-line workaround below succeeded with zero warnings
and errors; no project-wide analyzer settings were changed.

```powershell
dotnet build JobPortal.sln --no-restore "-p:NoWarn=CA1707%3BCA1859%3BCA1861"
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~JobUrlHashBackfillTests'
```

Focused result: 18 passed, 0 failed, 0 skipped, total 18.
Full non-database suite: **884 passed, 0 failed, 0 skipped, total 884**.
The three live PostgreSQL tests were excluded (not counted as skipped). Inspection
confirmed the other relational tests only inspect models/SQL or use fakes.
Database environment variables were removed only from the test child shell:

```powershell
Remove-Item Env:AIAPPLY_STEP5_POSTGRES -ErrorAction SilentlyContinue
Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName!~AIApplyDistributedPostgresTests' --logger 'console;verbosity=minimal'
```

Help and missing-environment CLI checks passed (missing configuration returned 2,
with the variable removed from the child shell before invocation; no connection).

Offline EF validation used a child-shell dummy localhost connection, never the
configured production secret:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Database=backfill_model_only;Username=postgres;SSL Mode=Disable'
dotnet ef migrations has-pending-model-changes --project JobPortal.Persistence.Postgres --startup-project JobPortal.API --context JobPortalDbContext --no-build
```

Result: no changes since the last migration. No database connection or migration
creation/application was performed. `git diff --check` passed (CRLF notice only).
