# PhonePe Sandbox and Production readiness

## Architecture and verified endpoints

Previously, DI fixed the HTTP base address to Sandbox and the gateway rejected any environment other than Sandbox. Production credentials therefore could not select production services. The singleton token cache also assumed one credential set.

The integration remains PhonePe Standard Checkout v2 with client credentials OAuth and O-Bearer authorization. Official documentation checked on 2026-10-06 establishes separate production OAuth and payment API bases:

| Operation | Sandbox | Production |
| --- | --- | --- |
| OAuth POST | `https://api-preprod.phonepe.com/apis/pg-sandbox/v1/oauth/token` | `https://api.phonepe.com/apis/identity-manager/v1/oauth/token` |
| Checkout POST | `https://api-preprod.phonepe.com/apis/pg-sandbox/checkout/v2/pay` | `https://api.phonepe.com/apis/pg/checkout/v2/pay` |
| Status GET | `https://api-preprod.phonepe.com/apis/pg-sandbox/checkout/v2/order/{merchantOrderId}/status` | `https://api.phonepe.com/apis/pg/checkout/v2/order/{merchantOrderId}/status` |

Sources: [Authorization](https://developer.phonepe.com/payment-gateway/website-integration/standard-checkout/api-integration/api-reference/authorization), [Initiate Payment](https://developer.phonepe.com/payment-gateway/website-integration/standard-checkout/api-integration/api-reference/create-payment/initiate-payment), [Order Status](https://developer.phonepe.com/payment-gateway/website-integration/standard-checkout/api-integration/api-reference/order-status), [Webhook](https://developer.phonepe.com/payment-gateway/website-integration/standard-checkout/api-integration/api-reference/webhook).

No PhonePe refund, subscription, settlement or other provider endpoint is implemented. AI Resume credit restoration is internal credit accounting, not a PhonePe refund.

## Implementation and validation

`PhonePeOptions.ResolveEndpoints` accepts only canonical `Sandbox` and `Production`, case-insensitively, and centralizes OAuth/API addresses. There is no default or arbitrary host override. Requests use absolute resolved URLs, so an incorrect HttpClient base address cannot reroute them. Credentials remain configuration values, independent of endpoint selection.

Configured PhonePe options validate on startup without network access. All seven settings below are required. Errors name settings without printing values. Unknown environments and placeholder values fail. Redirects reject embedded credentials, query strings and fragments. Production requires HTTPS and rejects loopback/localhost, including trailing-dot and subdomain forms. Sandbox preserves HTTP loopback support. An entirely absent PhonePe configuration permits unrelated application startup; attempting to use its gateway still fails closed.

OAuth still sends `client_id`, `client_secret`, `client_version`, and `grant_type=client_credentials`. Cached tokens are partitioned by a SHA-256 fingerprint of canonical environment and credentials/version. Partition locks prevent concurrent duplicate refreshes. Expiration and 401 refresh preserve environment selection. No credential or fingerprint is logged. Credential changes require the usual application restart/new gateway configuration; this is not a hot-reload feature.

Checkout keeps the existing merchant order ID, minor-unit amount, PG_CHECKOUT payload and configured redirect route `/payment/phonepe/return`. The existing returnTo allowlist remains. Status verification retains merchant-order, amount, state and successful transaction-reference checks. The 15-second HTTP timeout remains; automatic cross-host redirects and default HTTP client loggers are disabled to prevent forwarding/logging credentials. Provider error codes are sanitized and discarded if they reflect configured credentials, tokens or webhook authorization hashes. Response bodies and authorization headers are not logged.

## Webhook and entitlement security

The public route remains anonymous `POST /api/payments/phonepe/webhook`, with SHA authentication inside verification. SHA-256 of `username:password` is compared in fixed time against Authorization; existing SHA256-prefix compatibility remains. This integration does not switch to HMAC.

Select only `checkout.order.completed` and `checkout.order.failed`. These cover the existing membership and AI Resume credit purchases. Event names and browser redirects cannot grant entitlement: the backend independently obtains order status and verifies expected order, ownership, amount and completed transaction. Pending/failed statuses cannot grant entitlement even with a completed event name.

Membership replay handling and activation/expiry remain unchanged. Database concurrency tokens and unique event constraints protect competing writes; tests cover duplicate callbacks and concurrent webhook/return activation. AI Resume purchase verification retains its per-user transactional lock, paid-purchase short circuit and atomic ledger update; existing tests cover concurrent verification from separate contexts and exactly-once credit grants. No pricing or business service changed. AI Resume packages remain 1/5/10/25 credits for INR 19/79/129/249 respectively.

## Manual Render and dashboard configuration

Required variable names: `PhonePe__ClientId`, `PhonePe__ClientSecret`, `PhonePe__ClientVersion`, `PhonePe__Environment`, `PhonePe__RedirectBaseUrl`, `PhonePe__WebhookUsername`, `PhonePe__WebhookPassword`.

Placeholder-only production example:

```text
PhonePe__ClientId=<production-client-id>
PhonePe__ClientSecret=<production-client-secret>
PhonePe__ClientVersion=<production-client-version>
PhonePe__Environment=Production
PhonePe__RedirectBaseUrl=https://careerharbor.in
PhonePe__WebhookUsername=<webhook-username>
PhonePe__WebhookPassword=<webhook-password>
```

For Sandbox use `PhonePe__Environment=Sandbox` and the corresponding sandbox credentials. Do not mix credentials from the two environments.

In the PhonePe Business dashboard, use Production/Test Mode OFF, authentication type SHA, matching webhook username/password, and the two checkout events above. Public webhook URL:

`https://job-portal-lgic.onrender.com/api/payments/phonepe/webhook`

No Render variables, dashboard settings or deployment were changed by this implementation. Production credential validity, merchant onboarding and actual delivery remain to be verified manually after deployment. The synchronous webhook status check retains its existing 15-second timeout; PhonePe recommends a 3–5-second acknowledgement, so cold-start/network latency has not been proven under live conditions. No queue/outbox/schema redesign is included.

## Validation results and scope

Final relevant regression run: **235 passed, 0 failed, 0 skipped**. Counts: PhonePeEnvironmentTests 36; PhonePeGatewayTests 13; PortalMembershipTests 63; PaymentReturnPathTests 63; AIResumeWorkflowTests 28; MembershipPricingTests 13; PostgresPersistenceConfigurationTests 8; AIResumeMasterWorkflowTests 11. An earlier focused run passed 200 tests before three additional environment regressions were added; these runs overlap and must not be summed.

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:RunAnalyzersDuringBuild=false --filter 'FullyQualifiedName~PhonePeEnvironmentTests|FullyQualifiedName~PhonePeGatewayTests|FullyQualifiedName~PortalMembershipTests|FullyQualifiedName~PaymentReturnPathTests|FullyQualifiedName~AIResumeWorkflowTests|FullyQualifiedName~MembershipPricingTests|FullyQualifiedName~PostgresPersistenceConfigurationTests|FullyQualifiedName~AIResumeMasterWorkflowTests' --logger 'console;verbosity=minimal'
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore --no-incremental
```

Test compilation disables analyzers because of pre-existing unrelated test-suite analyzer errors (including CA1707/CA1859). API Release compilation uses normal analyzers. All PhonePe HTTP is mocked; PostgreSQL configuration tests do not establish live database concurrency behavior. No whole-backend-suite result is claimed.

Final Release API build: **succeeded, 0 warnings, 0 errors**, with normal analyzers, in 25.03 seconds.

Files changed by this pass only (the checkout already contains unrelated changes):

- `JobPortal.Infrastructure/Payments/PhonePeOptions.cs` (new)
- `JobPortal.Infrastructure/Payments/PhonePeGateway.cs`
- `JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.Application.Tests/PhonePeEnvironmentTests.cs` (new)
- `JobPortal.Application.Tests/PortalMembershipTests.cs` (three tests added)
- `docs/PRODUCTION_RELEASE_RUNBOOK.md` (PhonePe configuration guidance)
- `docs/phonepe-production.md` (this report)

No frontend, AI Resume tailoring logic, payment business services, membership prices, AI Resume prices, application settings, schema or migration was changed by this pass. No migration was created/run; no real PhonePe request/payment, secret insertion, commit, push, deploy or Render modification occurred. The backend is ready for production credentials to be supplied manually and deployment reviewed; live production operation is not claimed.
