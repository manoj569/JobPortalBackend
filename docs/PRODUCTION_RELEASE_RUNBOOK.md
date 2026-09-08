# Career Harbor production release runbook

This runbook covers the PostgreSQL/.NET 9 Career Harbor API release candidate. It does not authorize a production deployment. Substitute values only from the approved staging or production secret manager; never write secret values into this repository, command history, build arguments, image layers, or deployment logs.

## Release inputs and current constraints

- Build from an immutable reviewed commit, not an uncommitted working tree.
- Target `linux/amd64` using `JobPortal.API/Dockerfile` and the repository `JobPortal.API/seccomp_profile.json`.
- PostgreSQL migrations are owned by `JobPortal.Persistence.Postgres`. The manual `.github/workflows/migrate.yml` workflow targets the historical SQL Server project and must not be used for PostgreSQL.
- The application never applies migrations automatically.
- Razorpay currently accepts Test Mode key IDs only. PhonePe currently accepts the Sandbox environment only. Do not enable either as a live production payment provider until a separately reviewed production integration exists.
- Every external job-site adapter remains disabled until its separately authorized enablement gate passes.

## Configuration inventory

Use .NET environment-variable notation (`__` for `:`).

| Category | Setting | Classification | Requirement |
|---|---|---|---|
| Runtime | `ASPNETCORE_ENVIRONMENT=Production` | Required non-secret | Must be explicit. |
| Database | `ConnectionStrings__DefaultConnection` | Required secret | PostgreSQL connection with TLS and least-privileged runtime credentials. Migration credentials should be separate. |
| Authentication | `Jwt__Issuer`, `Jwt__Audience` | Required non-secret | Exact expected issuer and audience. |
| Authentication | `Jwt__Key` | Required secret | At least 32 characters, independently generated and rotated through the secret manager. |
| Authentication | `Otp__HashKey` | Required secret | At least 32 characters and distinct from the JWT key. |
| Google login | `Authentication__Google__Enabled`, `ClientId`, `AllowedCodeOrigins` | Optional non-secret | Keep disabled unless the complete configuration is approved. Origins must be exact HTTPS origins. |
| Google login | `Authentication__Google__ClientSecret` | Optional secret | Required only when Google login is enabled. |
| Data Protection | `AIApply__ExternalSessions__DataProtectionKeysPath` | Conditionally required non-secret | Absolute path on a persistent, access-controlled volume when external sessions are enabled. |
| Data Protection | `AIApply__ExternalSessions__RequirePersistentDataProtectionKeys=true` | Conditionally required non-secret | Mandatory outside Development when external sessions are enabled. |
| Data Protection | `DataProtectionCertificatePath` | Conditionally required non-secret | Certificate file must be mounted outside the image. |
| Data Protection | `DataProtectionCertificatePassword` | Conditionally required secret | Configure together with the certificate path. |
| AI Apply | `AIApply__Enabled` and worker, lease, concurrency, retry, reliability, cost and observability settings | Required non-secret when enabled | Start with validated bounded concurrency. Configuration validation fails startup for unsafe ranges. |
| Browser | `AIApply__Browser__Enabled`, `Headless=true`, timeouts | Required non-secret when enabled | `CaptureFailureScreenshot=false` and `AllowLoopbackForTests=false` in staging/production. |
| External sites | `AIApply__Sites__*__Enabled` | Optional non-secret | Default false. Enable only an adapter that has an independent authorized production validation. |
| External sessions | `AIApply__ExternalSessions__Enabled` and capture/transport limits | Optional non-secret | Requires persistent Data Protection keys; SignalR transport additionally requires configured sticky affinity. |
| External AI | `AIApply__AI__Enabled`, provider/model/embedding model | Optional non-secret | Keep disabled until an implemented provider and its secret-provider contract have been reviewed. |
| Payments | `Razorpay__KeyId`, `KeySecret`, `WebhookSecret` | Staging secret | Current code is Test Mode only; not a production payment configuration. |
| Payments | `PhonePe__ClientId`, `ClientSecret`, `WebhookUsername`, `WebhookPassword` | Staging secret | Current code is Sandbox only. |
| Payments | `PhonePe__ClientVersion`, `Environment`, `RedirectBaseUrl` | Staging non-secret | Environment must currently be `Sandbox`; redirect base must be the authorized HTTPS frontend origin. |
| Membership | `Membership__Plans__*` and AI Apply plan limits | Required non-secret | Review price, currency, duration and entitlements before rollout. |
| Email | `Email__Enabled`, `FromName`, `FromAddress`, `AppUrls__FrontendBaseUrl` | Required non-secret when enabled | Frontend URL must be the exact HTTPS public origin. |
| Email | `Email__Brevo__ApiKey` | Required secret when enabled | Secret manager only. |
| Job discovery | `JobDiscovery__Enabled`, schedule, provider IDs | Optional non-secret | Keep disabled until its provider and operational review pass. |
| Job discovery | `JobDiscovery__Adzuna__ApiKey` | Optional secret | Required only when its provider is enabled. |
| Frontend/CORS | `AppUrls__FrontendBaseUrl`, `Cors__AllowedOrigins` | Required non-secret | Exact HTTPS origins only; do not use wildcard origins with credentials. |
| Reverse proxy | `ReverseProxy__KnownProxies` | Required non-secret | Exact trusted proxy IPs; forwarded-header limit remains one. |
| Resume storage | `ResumeStorage__RootPath` | Required non-secret | Mount durable private storage when using local-file storage. Do not rely on the container writable layer. |
| Bootstrap admin | `BootstrapAdmin__Enabled` and identity fields | Optional non-secret | Disabled normally and immediately after an approved bootstrap. |
| Bootstrap admin | `BootstrapAdmin__Password` | One-time secret | Secret manager only; rotate after bootstrap. |
| Logging | Serilog levels and console sink | Required non-secret | Central collection must ingest structured stdout. A container-local file is not a durable monitoring sink. |
| Rate limiting | Code-defined global/auth/AI Apply/capture policies | Required code control | Validate at the edge and application layer; no secret. |
| Health | `/health/live`, `/health/ready` | Required non-secret | Configure liveness and readiness probes separately. |

## Pre-deploy

1. Record the reviewed branch, commit and clean `git status`. Confirm `git diff --check` passes.
2. Confirm no nested repository, logs, dumps, screenshots, browser state, local secrets, test Dockerfiles or fixture SQL is staged for release.
3. Run:

   ```text
   dotnet restore
   dotnet build -c Release --no-restore
   dotnet test -c Release --no-build --no-restore
   dotnet ef migrations has-pending-model-changes --project JobPortal.Persistence.Postgres --startup-project JobPortal.API --configuration Release --no-build
   ```

4. Build and record the immutable image digest:

   ```text
   docker build --platform linux/amd64 --file JobPortal.API/Dockerfile --tag <approved-registry>/careerharbor-api:<commit> .
   docker image inspect <approved-registry>/careerharbor-api:<commit>
   ```

5. Confirm the runtime user is UID/GID 10001, privileged mode is false, the repository seccomp profile is installed by the platform, `/dev/shm` is at least 1 GiB for browser-enabled instances, and Chromium launches without `--no-sandbox`.
6. Validate every required configuration entry above through the approved staging secret/config provider. Keep AI Apply, external sites, external sessions, optional providers and bootstrap disabled unless their prerequisites are satisfied.
7. Confirm centralized log ingestion, metrics/alert routing and an on-call destination are operational.
8. Record the currently deployed image digest as `<previous-image-digest>`.
9. Take and verify a provider-native PostgreSQL backup/snapshot. Record its identifier, retention, restore target and responsible operator. Do not proceed without a restorable backup.
10. Review `artifacts/CareerHarbor.Postgres.idempotent.sql`. It must contain 13 migration-history inserts through `20260904195443_AddAIApplyOperationalState`.

## Staging deploy

1. Place staging in maintenance/drain mode if required by the platform.
2. Run the reviewed PostgreSQL migration script once from a dedicated migration job using least-privileged migration credentials. Do not use the historical SQL Server GitHub workflow and do not run destructive `Down` migrations.
3. Verify all 13 migration IDs in `__EFMigrationsHistory` and confirm the latest ID.
4. Roll out the exact recorded image digest with rolling replacement. Do not rebuild during deployment.
5. Configure liveness as `/health/live` and readiness as `/health/ready`. Do not send traffic until readiness is HTTP 200.
6. Verify HTTPS scheme propagation, HSTS, trusted forwarded headers, exact CORS behavior and SignalR upgrade/affinity behavior.
7. Verify API startup, database connectivity, worker heartbeat, Chromium availability, external-session key persistence and structured log ingestion.
8. Use synthetic candidates and controlled non-employer URLs to verify authentication, entitlement, queue/claim, state transitions, NeedsAttention, NeedsReview, safe handoff and administrator operations. Never submit to a real employer or bypass CAPTCHA, MFA, OTP, anti-bot or access controls.

## Post-deploy observation

- Check `/api/admin/ai-apply/overview`, `/workers`, `/sites`, `/alerts`, `/failures` and `/costs` using an authorized staging administrator.
- Observe queue depth and age, unique claims, per-user/global concurrency, retries, dead letters, NeedsReview, submission uncertainty, browser failures, circuits, PostgreSQL readiness, rate-limit activity and provider failures.
- Confirm no sensitive values appear in logs and no browser processes or contexts leak after bounded work.
- Maintain heightened observation for at least one complete worker lease/recovery interval and one normal deployment monitoring window.

## Rollback conditions

Rollback or stop rollout for any of the following:

- Persistent readiness failure or abnormal API error rate.
- Migration failure or schema mismatch.
- Duplicate claim/execution/submission or corrupt queue state.
- Stale workers without safe takeover.
- Browser instability, process leakage or sandbox regression.
- External-session encryption/key persistence failure.
- Authentication, authorization, CORS, SSRF, secret-handling or sensitive-logging regression.
- Unexpected payment or provider behavior.

## Application rollback

1. Disable new AI Apply intake if queue safety is uncertain; do not delete queued records.
2. Gracefully drain/stop workers so leases can expire and recover normally.
3. Roll the application back to `<previous-image-digest>` using the staging platform's immutable-image rollback operation.
4. Confirm `/health/live` and `/health/ready`, worker heartbeat, queue uniqueness and browser cleanup.
5. Do not automatically reverse PostgreSQL migrations. The migration chain is designed for forward-compatible application rollback; if incompatibility is demonstrated, escalate to the database incident owner.

## Database recovery

- Preserve the failed database and logs for investigation.
- Prefer a corrected forward migration when safe.
- Restore the verified pre-deployment backup only with explicit incident authorization and an approved recovery point objective. Reconcile writes made after the backup before reopening traffic.
- Never execute generated `Down` migration SQL automatically in production.

## Worker, browser and secret recovery

- Worker: graceful stop first; verify stale heartbeat/lease expiry and single-owner takeover before re-enabling intake.
- Browser: stop the container, verify contexts/processes are gone, then replace it from the immutable digest. Never add `--no-sandbox` or privileged mode.
- Secret exposure: disable the affected integration, revoke/rotate the credential at its provider, update the secret manager, roll instances, and verify old credentials no longer work. Rotate JWT/OTP/Data Protection material only under their dedicated session/key continuity plans.

## Final GO checklist

GO requires all items below:

- Immutable clean source and image digest.
- Complete automated and PostgreSQL integration suites pass.
- Reviewed backup and 13-migration staging application pass.
- Real production-like staging deployment and synthetic smoke test pass.
- Central telemetry and alert delivery are proven.
- Production-capable payment/provider configuration is approved, or those features remain safely disabled without breaking required entitlements.
- Security and rollback drills pass with no unresolved P0/P1 issue.

Otherwise the decision is NO-GO.
