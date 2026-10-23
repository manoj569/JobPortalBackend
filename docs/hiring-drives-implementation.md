# Hiring Drives implementation report

Backend feature completed using the existing application/service/repository layers, FluentValidation registration, ApiResponse/PagedResponse wrappers, Administrator role, IAuditWriter, TimeProvider, base-entity audit timestamps and soft-delete filters. No frontend changes, sample campaigns, or fabricated hiring claims were added. Unrelated existing checkout changes were preserved.

## Files created

- JobPortal.Domain/Entities/HiringDrive.cs
- JobPortal.Application/Features/HiringDrives/HiringDriveContracts.cs
- JobPortal.Application/Features/HiringDrives/HiringDriveValidators.cs
- JobPortal.Application/Features/HiringDrives/HiringDriveService.cs
- JobPortal.Persistence/Configurations/HiringDriveConfiguration.cs
- JobPortal.Persistence/Repositories/HiringDriveRepository.cs
- JobPortal.API/Controllers/HiringDrivesController.cs
- JobPortal.API/Controllers/AdminHiringDrivesController.cs
- JobPortal.Persistence.Postgres/Migrations/20261023070836_AddHiringDrives.cs
- JobPortal.Persistence.Postgres/Migrations/20261023070836_AddHiringDrives.Designer.cs
- JobPortal.Application.Tests/HiringDriveTests.cs
- JobPortal.Application.Tests/HiringDriveMigrationTests.cs
- JobPortal.Application.Tests/HiringDrivePostgresTests.cs
- docs/hiring-drives.md
- docs/hiring-drives-implementation.md

## Files modified for this feature

- JobPortal.API/Program.cs: shared analytics write rate limit.
- JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs: service registration; validators use existing assembly scanning.
- JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs: repository registration.
- JobPortal.Persistence/Context/JobPortalDbContext.cs: HiringDrives DbSet.
- JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs: HiringDrive mapping, preserving the existing model.

## Endpoints added

| Method | Route | Access |
|---|---|---|
| GET | /api/public/hiring-drives | Anonymous |
| GET | /api/public/hiring-drives/home | Anonymous; only explicitly selected homepage campaigns |
| POST | /api/public/hiring-drives/{id}/click | Anonymous, rate limited |
| POST | /api/public/hiring-drives/impressions | Anonymous, rate limited, bounded body/batch |
| GET | /api/admin/hiring-drives | Administrator, paginated |
| GET | /api/admin/hiring-drives/{id} | Administrator |
| POST | /api/admin/hiring-drives | Administrator |
| PUT | /api/admin/hiring-drives/{id} | Administrator |
| DELETE | /api/admin/hiring-drives/{id} | Administrator, soft delete |

## Database changes

Migration: `20261023070836_AddHiringDrives`.

Homepage placement migration: `20261023110156_AddHiringDriveHomepagePlacement`, adding `ShowOnHome` with a database default of false and a filtered placement index.

Adds only HiringDrives, with all editable campaign fields, base audit/soft-delete fields, creator/editor references, bigint ClickCount and ImpressionCount. Adds restrictive Company, Job and User foreign keys, indexes for active scheduling and display order, and checks for date ordering, nonnegative order/counters, and presence of a destination. Existing migrations were not edited. No data is seeded. Migration rollback drops only HiringDrives.

Click destination selection and increment run in a transaction with a PostgreSQL row lock. Concurrent increments use ExecuteUpdate rather than read/modify/write counters. Admin changes cannot overwrite analytics counters. Impressions update eligible distinct IDs atomically without storing visitor identities or IP addresses.

## Tests and results

- HiringDriveTests: UTC visibility boundaries, active/inactive/future/expired/no-end cases, display/newest ordering, deleted filtering, URL schemes/credentials/lengths, required fields/destination, company/job existence, internal preference, public field whitelist, audit metadata, soft deletion, CTR, request bounds, and Administrator authorization policy checks for anonymous/Candidate/Recruiter/Administrator users.
- HiringDriveMigrationTests: scoped Up/Down operations, constraints, FK deletion behavior, SQL generation, and model/snapshot agreement.
- HiringDrivePostgresTests: all migrations applied in a fresh disposable schema; actual SQL click/impression execution; duplicate and invalid/inactive/expired/deleted IDs; authoritative destination; twelve parallel click increments without lost counts.
- Focused feature suite: **28 passed, 0 failed, 0 skipped**, including the PostgreSQL test.
- Nearby regression suite (SupportTicketTests, SupportTicketMigrationTests, AdminCompanyCreationTests, CompanyLogoTests): **107 passed, 0 failed, 0 skipped**.
- Normal API build: **passed, 0 warnings, 0 errors**, including production project analyzers.
- Requested solution build (`dotnet build JobPortal.sln --no-restore -m:1`): **blocked by 197 pre-existing analyzer errors in unrelated test files**. Examples include CA1707 test names, CA1861 arrays, CA1869 serializer options, and xUnit2031 assertions. No errors refer to HiringDrive files. Existing unrelated tests were not edited to suppress them.
- The test assembly was compiled with `-p:RunAnalyzers=false` to execute tests despite that unrelated analyzer backlog. This setting was used only on the command line; project analyzer settings are unchanged.
- `git diff --check`: passed.

Evidence logs in the repository working folder: hiring-drives-api-final.log, hiring-drives-final-build.log, hiring-drives-tests-focused.log, hiring-drives-postgres-tests.log, hiring-drives-regression.log. TRX results are retained in the sibling hiring-drives-results folder.

PostgreSQL validation used a dedicated loopback-only cluster on port 55450 and database careerharbor_workday_test, with the existing fixture's disposable schema isolation. The server was stopped afterward. No application or production database connection was used.

## Assumptions and deployment

- Administrators verify hiring claims and the authenticity of external employer application links before activation. HTTPS syntax validation is not proof of authenticity; the API does not fetch outbound URLs.
- Both JobId and ApplyUrl may be saved, with internal JobId taking precedence. Drives targeting soft-deleted internal jobs are suppressed. Company name/logo comes from CompanyId without duplicate fields.
- Optional BadgeType values are hiring, walkIn, openings, featured. ImageUrl also requires HTTPS. Text remains plain text; consuming UI must use safe text rendering.
- All campaign timestamps must be explicitly UTC. Start/end boundaries are inclusive for public retrieval. End must be later than start when saving.
- Analytics counts accepted events, not unique people; duplicate IDs count once per impression batch. The shared limit is 30 click/impression requests per minute per remote IP per application instance. Repeated accepted requests and multiple application instances can increase counts; there is no claim of bot-proof analytics.
- Apply the new migration through the existing PostgreSQL deployment workflow before using these endpoints. No new keys, services or scheduled jobs are required. Existing trusted proxy configuration must correctly resolve the client IP for rate limiting.
- Frontend integration remains outside this task: it should send impressions only for visible cards, call click before navigation, and use the returned authoritative destination.
