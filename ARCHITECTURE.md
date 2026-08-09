# Job Portal Architecture

## Dependency direction

The solution follows a layered architecture:

```text
Domain       Shared
   \          /
    Application
      /     \
Infrastructure Persistence
         \   /
           API
```

- `JobPortal.Domain` contains entities, enums, and domain constants. It has no framework dependencies.
- `JobPortal.Shared` contains transport-neutral response models.
- `JobPortal.Application` owns use cases, validation, DTOs, and dependency abstractions.
- `JobPortal.Infrastructure` implements external concerns such as JWT, hashing, SMTP, and Razorpay.
- `JobPortal.Persistence` implements EF Core repositories, mappings, migrations, and the unit of work.
- `JobPortal.API` is the composition root and HTTP transport.

References must continue to point inward. Domain code must not reference EF Core, ASP.NET Core, or infrastructure implementations.

## API conventions

- Public endpoints use `/api/jobs`.
- Authenticated user endpoints use `/api/dashboard`, `/api/memberships`, and `/api/payments`.
- Candidate-only profile, resume, saved-job, and application endpoints use `/api/candidate`.
- Administrator endpoints use `/api/admin`.
- Collection endpoints are paginated and capped at 100 items.
- All database and external I/O is asynchronous and accepts a `CancellationToken`.
- Expected failures use `AppException`; unhandled implementation details are never returned to clients.
- UTC timestamps use the `Utc` suffix. Services use `TimeProvider` for deterministic testing.

## Data access

- Read-only queries use `AsNoTracking` and project to response DTOs in SQL.
- Sorting fields are allow-listed; user input is never interpolated into SQL.
- Soft-delete query filters are enabled for all `BaseEntity` types.
- Payment and membership transitions use SQL Server row-version concurrency tokens.
- SQL retry is limited to transient failures; command timeout is 30 seconds.
- The DbContext is pooled. Scoped services must never retain entity or DbContext references beyond a request.
- Migrations are the only supported schema-change mechanism.

## Security boundaries

- JWT signing keys, Razorpay secrets, SMTP credentials, and production connection strings belong in environment variables or a secret manager.
- Razorpay amounts and plan duration are server-controlled.

## Initial Administrator bootstrap

The API can create the first Administrator through a disabled-by-default, idempotent startup initializer.
Configure `BootstrapAdmin:Enabled`, `Email`, `Password`, `FirstName`, and `LastName` through
.NET User Secrets locally or a production secret manager. Never place the credentials in an
appsettings file. Set `BootstrapAdmin:Enabled` back to `false` after the first Administrator has
been created. The initializer does not apply database migrations and will never elevate an
existing non-Administrator account.
- Payment signatures use constant-time verification.
- Password changes and mobile-OTP resets revoke active refresh tokens.
- Authentication endpoints have stricter per-client rate limits.
- Output caching is restricted to anonymous public-job reads and varies by query and origin.
- Forwarded headers are accepted only from configured trusted proxies.

## Candidate profiles and applications

Candidate endpoints require the `Candidate` role and re-check that the current account is Active.
Every profile, resume, saved-job, and application query is scoped by the authenticated user's
identifier; client-supplied candidate identifiers are never accepted.

Public Candidate registration accepts only `fullName`, email, password, a ten-digit Indian mobile
number, and explicit Terms/Privacy consent. Full names are Unicode-aware and split at the first
space without fabricating a surname. Email is trimmed and lowercase-normalized; mobile numbers
are canonicalized to `+91XXXXXXXXXX`. User uniqueness remains enforced by the existing filtered
email/mobile indexes. Public clients cannot select a role or set account, confirmation,
membership, payment, or audit state.

Registration writes a short-lived `PendingRegistration` containing only a PBKDF2 password hash,
plus a purpose-scoped `OtpChallenge` containing an HMAC-SHA256 OTP digest. It does not create a
`User` until the six-digit registration OTP is verified. Challenges expire after five minutes,
allow five failed attempts, enforce a 60-second resend cooldown, and rotate the digest when
resent. Successful verification atomically creates an Active, phone-confirmed Candidate without
issuing JWTs. Duplicate and unknown identities receive privacy-safe responses.

- `POST /api/auth/register`
- `POST /api/auth/verify-registration-otp`
- `POST /api/auth/resend-registration-otp`
- `POST /api/auth/login` accepts an email or Indian mobile identifier plus password.
- `POST /api/auth/request-login-otp`
- `POST /api/auth/login-with-otp`
- `POST /api/auth/request-password-reset`
- `POST /api/auth/complete-password-reset`

Mobile login OTP is restricted to Active Candidates and uses a distinct purpose from registration.
Password reset is email-based and returns the same HTTP 202 response for existing, inactive, and
unknown accounts. Active users receive a cryptographically random, 30-minute link through the
configured SMTP provider. Only a SHA-256 token digest is persisted. Successful completion replaces
the password hash, clears the reset digest and expiry, and revokes all active refresh tokens. Reset
tokens, passwords, email bodies, reset URLs, and SMTP credentials are excluded from logs and audit
metadata. IP rate limits are applied at the API boundary, while mobile cooldowns, send limits,
expiry, and attempt limits remain enforced for registration and login OTP challenges.
During migration, recognizable legacy numbers are canonicalized deterministically; duplicate or
malformed legacy phone values are cleared without changing account role, status, or login access.
Existing users retain password/email login. The mobile-OTP migration lowercase-normalizes their
normalized email values and marks existing Candidates with stored normalized mobile numbers as
phone-confirmed. Historical OTP reset-challenge columns and migrations remain intact for schema
compatibility, while the `User` password-reset digest and expiry columns power the active email flow.

- `GET|PUT /api/candidate/profile`
- `GET|PUT /api/candidate/onboarding`
- `PUT|GET|DELETE /api/candidate/resume`
- `GET /api/candidate/saved-jobs`
- `PUT|DELETE /api/candidate/saved-jobs/{jobId}`
- `POST /api/candidate/jobs/{jobId}/applications`
- `GET /api/candidate/applications`
- `GET /api/candidate/applications/{applicationId}`
- `POST /api/candidate/applications/{applicationId}/withdraw`

Resume storage is abstracted behind `IResumeStorage`. The default local implementation generates
opaque server-side keys and writes beneath `ResumeStorage:RootPath`, which must not be inside a
`wwwroot` path. Uploads are limited to 5 MB and require matching PDF, legacy DOC, or DOCX
extension, media type, and file signature. Production deployments should configure a persistent
private volume or replace the implementation with private object storage, and should add malware
scanning and retention policies. Resume files referenced by submitted applications are retained
when a candidate replaces or removes the current resume.

Applications require an active portal-wide membership and an available published, visible,
unexpired job. A unique database index prevents any duplicate application for a candidate/job
pair. Candidates can withdraw only applications still in `Submitted` status.

Candidate onboarding supports Student, Fresher, and Experienced career stages; multiple desired
opportunities and work preferences; city, skills, education summary fields, graduation year, and
years of experience. Submission is optional for existing Candidates and does not gate login, job
viewing, saved jobs, or application access. A valid submission records its UTC completion time.
Audit rows contain only the names of changed onboarding fields and completion state, never the
submitted values, email, mobile number, password, token, or resume data.

## Administrator job lifecycle

All job-management routes under `/api/admin/jobs` require the exact `Administrator` role. The
paginated list supports company, category, status, featured, expiry-range, and keyword filters;
detail and update operations use the existing validated job DTOs.

- `GET /api/admin/jobs`
- `GET /api/admin/jobs/{id}`
- `PUT /api/admin/jobs/{id}`
- `POST /api/admin/jobs/{id}/publish`
- `POST /api/admin/jobs/{id}/unpublish`
- `POST /api/admin/jobs/{id}/close`
- `POST /api/admin/jobs/{id}/archive`
- `POST /api/admin/jobs/{id}/feature`
- `POST /api/admin/jobs/{id}/unfeature`

Publishing revalidates the complete job, its non-deleted company and category, and a mandatory
future expiry date. Unpublishing returns a Published job to Draft; closing produces Closed;
archiving is final. Feature status is removed whenever a job is unpublished, closed, archived,
hidden, or automatically expired. Public output-cache entries are evicted after administrator
lifecycle changes.

`JobExpiryHostedService` runs a configurable UTC cycle (`JobExpiry:Enabled`,
`IntervalMinutes`, and `RunOnStartup`). It performs one conditional database update from
Published to Expired for overdue rows, making repeated runs idempotent, and does not run
migrations. Only visible Published jobs with a future expiry are eligible for public, related,
featured, saved-job, or new-application queries. Existing applications remain queryable after
the associated job closes or expires.

## Administrator CSV imports

CSV import endpoints require the exact `Administrator` role and accept one UTF-8 `.csv` file in
the multipart field named `file`:

- `POST /api/admin/imports/companies/preview`
- `POST /api/admin/imports/companies/commit`
- `POST /api/admin/imports/jobs/preview`
- `POST /api/admin/imports/jobs/commit`
- `GET /api/admin/imports/templates/companies`
- `GET /api/admin/imports/templates/jobs`

Uploads are limited to 5 MB and 500 data rows. Headers are case-insensitive but must contain the
complete documented set exactly once; missing, duplicate, empty, and unknown headers are rejected.
The parser uses RFC 4180 quoting through CsvHelper and rejects malformed or non-UTF-8 content.
Preview performs parsing, reference resolution, duplicate detection, and FluentValidation without
tracking additions or calling the unit of work. Commit requires the same file again and repeats all
validation. If any row is invalid, commit returns the detailed row results without attaching or
saving any company, job, or audit change. Otherwise all actionable rows and one counts-only audit
event are persisted by a single `SaveChanges` transaction; duplicate rows are skipped.

Company headers and fictional template example:

```csv
name,websiteUrl,industry,location,employeeCount,description,isVerified
Example Learning Labs,https://example.invalid,Education,"Pune, Maharashtra",120,"Fictional company for import testing",false
```

Companies match by generated normalized slug or normalized name. Existing rows are explicitly
reported as `Update existing`; repeated CSV rows are `Skip duplicate`. Name is updated, while a
non-empty optional cell updates only its corresponding safe company field. Blank optional cells do
not erase existing values. New companies are always unverified, and the CSV `isVerified` value can
never verify or unverify an existing company.

Job headers and fictional template example:

```csv
title,companyName,categoryName,description,applicationUrl,employmentType,workplaceType,experienceLevel,location,minSalary,maxSalary,currencyCode,expiresAtUtc,responsibilities,requirements,benefits,isFeatured
Example Software Intern,Example Learning Labs,Technology,"Fictional role for template testing",https://jobs.example.invalid/apply/example-role,Internship,Hybrid,Entry,Pune,,,INR,,"Assist with sample projects","Basic programming knowledge","Learning allowance",false
```

Enum cells use the API enum names documented by Swagger, such as `FullTime`, `Remote`, and `Mid`.
Job imports resolve existing companies and categories only; missing or ambiguous references are row
errors. Duplicate identity is company plus title plus application URL. Every imported job is forced
to `Draft`, `IsHidden=false`, `IsFeatured=false`, and `PublishedAtUtc=null`, regardless of the
`isFeatured` cell. Recruiter contacts are not part of either the import contract or template.
Imported jobs must be reviewed and published through the existing lifecycle endpoints.

## Administrator application review

Application-review endpoints require the exact `Administrator` role:

- `GET /api/admin/applications` accepts job, company, category, status, submitted-date, and
  keyword filters together with capped pagination.
- `GET /api/admin/applications/{applicationId}` returns the review-safe candidate profile, job
  summary, cover letter, current status, and status history.
- `GET /api/admin/applications/{applicationId}/resume` streams the application-time resume
  snapshot from private storage. Storage keys and public resume URLs are never returned.
- `PUT /api/admin/applications/{applicationId}/status` supports `Reviewed`, `Shortlisted`, and
  `Rejected` under the application transition rules.

Every transition records its previous and new status, the authenticated actor, UTC timestamp,
and an optional administrator-only note. Candidate application DTOs do not include status
history or internal notes. Shortlist and rejection notification attempts run only after the
database commit and receive no internal-note value, so an SMTP failure cannot revert the review.
For production-scale delivery, replace direct SMTP notification with a durable transactional
outbox and worker.

To exercise the flow in Swagger, sign in as an Administrator, authorize with the access token,
list applications to obtain an identifier, open its detail, optionally download its resume, then
send `{"status":"Reviewed","internalNote":"Reviewed in Swagger"}` to the status endpoint.
Follow with `Shortlisted` or `Rejected`; final and withdrawn applications must return a conflict.

## Append-only audit logging

Successful sensitive mutations in Administrator management, Candidate profile/resume/saved-job/
application flows, and Razorpay payment/membership flows append an audit row in the same unit of
work as the business state change. Each new event records the actor identifier and role, action,
entity type and identifier, UTC occurrence time, and the request correlation identifier.
Metadata is deliberately allow-listed and bounded; credentials, tokens, payment signatures,
provider identifiers, request bodies, resume data and paths, internal review notes, and profile
content are never included.

`GET /api/admin/audit-logs` requires the exact `Administrator` role and supports combined actor,
action, entity type, entity identifier, UTC date-range, and correlation-ID filters with pagination
capped at 100 rows. It returns a safe DTO and has no update or delete companion route. The audit
repository likewise exposes only append and search operations. EF change tracking rejects audit
updates/deletes, while migration `AddSecureAppendOnlyAuditLogging` adds a SQL Server
`INSTEAD OF UPDATE, DELETE` trigger as a database-level backstop. Existing rows are not altered or
backfilled, so no historical actions are invented.

Applications should propagate a non-sensitive `X-Correlation-ID` (letters, digits, `.`, `_`, and
`-`, at most 64 characters); otherwise the server trace identifier is used. Database principals
used by the API should not have permission to disable the trigger. For stronger production
tamper-evidence and long-term retention, stream audit records to access-controlled immutable/WORM
storage or a security information and event management system.

## OTP SMS, legal content, and transactional email

`ISmsService` is the provider boundary for registration and login OTP delivery.
`Otp:HashKey` and real provider credentials belong in User Secrets or environment variables.
`Fast2SmsService` sends enabled OTP traffic to Fast2SMS over its HTTPS `bulkV2` endpoint
using a typed `HttpClient` with a 15-second timeout. Configure `Sms:Fast2Sms:ApiKey` only through a
secret store. Logs contain the OTP purpose, HTTP outcome or safe failure category, and at most the
destination's final four digits; they never contain the API key, OTP, complete number, request body,
or provider response body. Safe result categories distinguish disabled/configuration/input failures,
provider HTTP failures or rejection, timeouts, network failures, unexpected exceptions, and successful
delivery. `AuthService` separately records each provider result and cooldown/rate-limit skips without
changing privacy-safe API responses. Automated tests use an in-memory fake sender or HTTP handler.
All validation and database work honors the incoming request cancellation token. Once an OTP
challenge has been committed, delivery switches to a 20-second bounded token linked to application
shutdown, so a Swagger/browser disconnect cannot strand a durable challenge. Provider `HttpClient`
still enforces its stricter 15-second timeout.

`GET /api/legal/terms-of-use` and `GET /api/legal/privacy-policy` anonymously return application-
owned versioned, effective-dated plain-text content. The accepted legal version is stored for new
registrations. Application-status messages and password-reset links use `IEmailService` and direct
SMTP. Password-reset links are built from `AppUrls:FrontendBaseUrl` plus `/reset-password`, with
URL-encoded email and token parameters. Render production configuration uses
`AppUrls__FrontendBaseUrl`, `Email__FromName`, `Email__FromAddress`, `Email__Enabled`, `Email__Smtp__Host`,
`Email__Smtp__Port`, `Email__Smtp__EnableSsl`, `Email__Smtp__Username`, and
`Email__Smtp__Password`. Brevo SMTP
credentials and the sender address remain server-side configuration only. A durable transactional
outbox should be considered before scaling production email delivery.

## Razorpay Test Mode payments

The only purchasable plan is a portal-wide ₹99 INR Candidate membership lasting 30 days.
Payment orders are first persisted locally in `Created` state and become `Pending` only after
Razorpay returns matching server-requested order details. Checkout confirmations, raw-body
webhooks, and reconciliation all pass through `IRazorpayGateway`; membership is activated or
extended only after a verified signature or a provider-confirmed captured payment.

Provider order IDs, payment IDs, and webhook event IDs remain globally unique even if a local
record is soft-deleted. Candidate reads and confirmations are owner-scoped. The webhook is
anonymous only at the HTTP authentication layer and rejects every request without a valid
Razorpay webhook HMAC. Configuration and manual testing are documented in
`RAZORPAY_TEST_MODE.md`. Refunds remain a future audited administrative workflow.

## Candidate job search and filter facets

`GET /api/jobs` remains the single public browsing endpoint. It always starts from an
`AsNoTracking` eligible-job query: Published, visible, not deleted, published at a known UTC time,
and either without an expiry or expiring after the current `TimeProvider` UTC value. Keyword,
multi-select location/work mode/company/industry/category metadata, persisted experience and pay
ranges, internship duration, education, poster type, freshness, and featured filters compose on
that database query. The default deterministic order is featured first, then latest published;
explicit latest-added, closing-soon, and salary orders are also database-side.

`GET /api/jobs/filter-options` accepts the same filters and returns only values and counts derived
from currently eligible jobs. Each facet omits its own selected dimension while retaining all other
filters, so alternatives remain useful without fake or stale counts. Facets use a fixed set of
grouped aggregate queries rather than per-job loading. The public summary/details projections do
not include recruiter contacts, application URLs, hidden/deleted state, or other administrator-only
fields.

Migration `AddCandidateJobSearchFilters` adds nullable persisted metadata to Jobs and Companies,
plus range constraints and targeted indexes. Existing records are deliberately not assigned made-up
experience, education, duration, department, role, poster, or company-type values. Administrator
create/update contracts expose the new fields as optional. CSV import keeps its legacy headers
required and treats the new columns as optional, preserving existing files while the downloadable
templates include the richer schema. All imported jobs still start as visible, unfeatured,
unpublished Draft records.

## Scaling guidance

- API instances are stateless and can scale horizontally.
- The current output cache is per process. Use a distributed output-cache store when deploying multiple instances if cache consistency becomes important.
- File logging is suitable for local/single-node operation. Production deployments should ship structured logs to a centralized sink.
- For large job catalogs, replace `Contains` keyword search with SQL Server full-text search or an external search index.
- Revenue remains grouped by currency; cross-currency totals require an explicit exchange-rate service and accounting date.

## Build policy

`Directory.Build.props` enables recommended .NET analyzers and treats every warning as an error. Generated EF migrations have only the generated-code allocation rule suppressed. A successful build must report zero warnings.
