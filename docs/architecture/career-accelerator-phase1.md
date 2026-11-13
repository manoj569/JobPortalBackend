# Career Accelerator: Phase 0 decisions and Phase 1 plan

Baseline: SDK 10.0.403 / net9.0; 3,372 passed, 0 failed, 95 explicitly skipped. The checkout contains uncommitted build/test fixes. They are preserved on `codex/accelerator-phase1`; no clean-checkout claim is made. Builds and tests run sequentially.

## Integration decisions

- Keep the modular monolith: Domain entities, Application feature services/contracts, Persistence EF store/configurations, API controllers/hosted maintenance.
- Reuse JWT candidate/admin authorization, active `User`/`Role`, existing uploaded resume identity/version, canonical `Skill`, `JobPortalDbContext`, `IAuditWriter`, `ApiResponse`, and existing exception handling. No identity, resume blob, or gateway duplication.
- Existing portal memberships permit one active portal entitlement per user and drive existing job/AIApply access. Accelerator billing cycles are separate product entitlements referencing existing verified `Payment` rows. Never infer access from ₹99/₹299 memberships or a client callback.
- Reuse `IMembershipPlanProvider.GetGstRate()` and `MembershipPricing` for configurable tax calculation. Base fee defaults to INR 278, independently configurable. Checkout is explicitly unavailable in Phase 1; payment routing/webhooks/renewals belong to Phase 6.
- Allowances align to an entitlement billing cycle: `[verified PaidAtUtc, PaidAtUtc.AddMonths(1))`, UTC, never calendar resets. Allocations are snapshotted per cycle. Future renewal must retain the original billing anchor to avoid month-end drift; overlapping cycles are rejected.
- Taxonomy versions are immutable published snapshots referencing canonical skill IDs. Parent/prerequisite edges must stay in the same version and form a DAG. Roles are data, not a technology-specific enum. Self-reported proficiency is not assessed evidence.
- Profiles and goal histories belong to the existing candidate. Goal role/version never changes in place; create another goal to change careers. No roadmap/progress/evidence is invented in Phase 1.
- PostgreSQL transaction-scoped, feature-namespaced global advisory lock serializes entitlement and quota writes, including global cost budgets. This intentionally favors correctness over write throughput; no lock spans provider/network I/O. The existing execution-strategy pattern handles transient transaction retries. Revision tokens protect profile/goal updates. Nonrelational tests use an explicitly test-only semaphore, not a production concurrency claim.
- Reserve one feature unit and the maximum bounded model cost before dispatch. A key is user-scoped and payload-bound across features. Release only before dispatch; timeouts after dispatch remain uncertain and charged/held pending reconciliation. Never automatically retry a potentially chargeable call. Expired unused reservations release capacity; dispatched expirations remain uncertain. Dispatch across UTC-day/cycle boundaries is rejected so a reservation cannot bypass daily budgets.
- AI provider-neutral contracts carry correlation, task/model, structured output, tokens and actual cost. Live calls are not implemented in Phase 1. Configuration defaults disable both the module and AI. No provider prices or credentials are invented. Phase 3 must implement schema validation, redaction, bounded context, provider availability/circuit policies and deterministic fakes before enabling live dispatch.
- Retention maintenance releases stale reservations and removes personal profile text/resume links for deleted accounts. Private queries also check active candidate identity. Financial/usage/audit records contain no prompt or response content and remain retained; a legal retention policy for those records is a deployment prerequisite.

## Reviewable implementation sequence

1. Entities, bounded DTOs/options and repository/AI contracts.
2. Immutable taxonomy publication, private profile/goals, verified-payment entitlement seam, atomic usage reservations and append-only events.
3. EF constraints/indexes, repository transaction boundary, candidate/admin controllers and bounded maintenance worker.
4. Generate an additive PostgreSQL migration, inspect every operation and generate SQL without applying it to a shared/production database.
5. Focused authorization, validation, replay, quota/cost/concurrency/recovery/privacy tests and opt-in disposable PostgreSQL verification; then the complete feasible suite.

## Phase boundary

No diagnostics, roadmap generation, tutor, assignment execution, interviews, readiness scores, Skill Passport, checkout, or live AI endpoint is advertised as implemented. No arbitrary submitted code executes. Phase 2 needs versioned attempts/questions, deterministic scoring/evidence and roadmap versions that reference these immutable goals/taxonomy snapshots. Approval is required before Phase 2 work.
## Local verification environment

The repository targets `net9.0` and is verified with .NET SDK `10.0.403`; do not change target frameworks to work around local SDK installation problems.

Some local SDK installations are missing the workload resolver locator SDK folders (`Microsoft.NET.SDK.WorkloadAutoImportPropsLocator` and `Microsoft.NET.SDK.WorkloadManifestTargetsLocator`). That is an SDK installation defect, not a project dependency. When no .NET workloads are required, use this command-scoped workaround until the SDK is repaired:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet restore JobPortal.sln
dotnet build JobPortal.sln --no-restore /m:1 /nr:false
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore
```

`/m:1 /nr:false` avoids the local compiler-server named-pipe access restriction observed in restricted execution environments. Remove the environment variable after the command session; it is not application configuration and is not committed.

## Disposable PostgreSQL verification

PostgreSQL integration is opt-in and must use a locally controlled database whose name ends in `_test`; it must never use Neon, production, or a shared environment. Before any migration command, inspect the connection string and confirm the database name, host, and credentials are local/disposable. A safe shell setup is:

```powershell
$env:CAREER_ACCELERATOR_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=careerharbor_accelerator_test;Username=postgres;Password=<local-only-password>'
$env:ConnectionStrings__DefaultConnection = $env:CAREER_ACCELERATOR_TEST_POSTGRES
```

With that explicit confirmation, first generate and inspect the SQL for `AddCareerAcceleratorPhase1`, then apply it only to the `_test` database:

```powershell
& .\.tools\dotnet-ef migrations script --project JobPortal.Persistence.Postgres --startup-project JobPortal.API --context JobPortalDbContext
& .\.tools\dotnet-ef database update --project JobPortal.Persistence.Postgres --startup-project JobPortal.API --context JobPortalDbContext
```

The Phase 1 migration is additive: it creates only Accelerator tables, constraints, and indexes. It does not modify existing tables, seed data, or execute raw SQL. PostgreSQL-specific tests must verify advisory-lock serialization, unique idempotency keys, append-only usage events, verified-payment entitlement isolation, profile erasure, and candidate ownership before a Phase 1 production decision.

