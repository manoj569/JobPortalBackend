# Generic employer integration

Inspected local main and origin/main at b2f52b9 before editing. The checkout is feature/generic-employer-crawler-backend at 49f9034; these integration files match main before this fix. Pre-existing referral changes were left intact.

Custom sources created/updated with the frontend identifier `employer-direct` (or no identifier) normalize to `employer-direct:auto-v1`. Existing stored aliases execute without a migration. Scheduler selection and duplicate detection recognize both identifiers. Other Custom identifiers retain their value. Explicit legacy `employer-direct-v1` snapshot payloads retain the existing bound, incomplete-only path; malformed/nonempty payloads do not fall through to live acquisition.

## Production configuration

Set these on the .NET API service that hosts JobSourceRunWorker:

- `EmployerAcquisition__Endpoint`: the acquisition service base URL, e.g. `https://acquisition.example.com`. The scanner posts to the root path `/employer/scan`; any base path is replaced. HTTPS is required except for loopback HTTP. URL credentials are rejected.
- `EmployerAcquisition__Token`: the acquisition service bearer token, at least 32 characters, matching the token configured at that service.

These names come from binding the `EmployerAcquisition` configuration section to `EmployerAcquisitionOptions.Endpoint` and `.Token`. Do not substitute single underscores or the inventory-scanner settings.

**The acquisition worker must be deployed separately and reachable from the API.** This repository's .NET scanner is an HTTP client; it does not start or host `/employer/scan`. The existing .NET JobSourceRunWorker orchestrates durable runs but does not implement acquisition. No acquisition worker server implementation/deployment is tracked in this repository, so its server-side token variable name cannot be established here. Deploy/configure the compatible worker with its SafeWeb public-address validation and pinning protections, and match its bearer secret with the .NET token above. API deployment alone is insufficient.

The fixed operator endpoint, redirect/proxy/cookie restrictions, durable run requirement, response source/company/URL/run binding, response limits and authoritative/incomplete reconciliation rules are preserved. No error masking or frontend change is required. Tests use a simulated worker response; production reachability/credentials are not exercised.

## Verification

Focused execution: 65 passed, 0 failed, 0 skipped, covering GenericEmployerContractTests, EmployerDirectIntegrationTests, JobSourceAdministrationTests and JobSourceManualRunTests. Command: `dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-restore -p:RunAnalyzers=false --filter 'FullyQualifiedName~GenericEmployerContractTests|FullyQualifiedName~EmployerDirectIntegrationTests|FullyQualifiedName~JobSourceAdministrationTests|FullyQualifiedName~JobSourceManualRunTests' -v minimal`.

The initial default test build encountered existing unrelated analyzer errors (including CA1707 in UrlCanonicalizerTests and xUnit2031 in PortalMembershipTests); the execution command disables analyzers only for that invocation. Repository analyzer policy is unchanged. The CA1860 finding in the new test was corrected. `git diff --check` passed. No commit, push, migration, production row change or deployment was performed.

Production build: `dotnet build JobPortal.API/JobPortal.API.csproj --no-restore -v minimal` passed with normal analyzers enabled, 0 warnings and 0 errors.
