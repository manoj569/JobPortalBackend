# Help & Support backend

The support feature uses the existing application services, repositories, `JobPortalDbContext`, UTC `BaseEntity` audit/soft-delete fields, JWT identity, Administrator role, API envelopes, exception handling, rate limiter, and Brevo email service. No packages were added.

## Configuration and deployment

Set `SupportSettings__SupportEmail` to the support team's mailbox. Existing `Email__Enabled`, `Email__FromAddress`, `Email__FromName`, `Email__Brevo__ApiKey`, and `AppUrls__FrontendBaseUrl` settings continue to control email delivery. A missing support mailbox or disabled/failed email delivery is logged without rejecting a saved ticket. A configured invalid mailbox is rejected at startup.

Set `SupportSettings__ScreenshotRootPath` to a private, writable **persistent disk** directory outside `wwwroot`. All API instances must share that directory when running multiple replicas. Back up this directory together with the database. The fallback is `support-screenshots` under the existing `ResumeStorage:RootPath`, or `private-storage/support-screenshots` under the application directory if no resume root is configured; ephemeral hosting storage is unsuitable for production screenshots.

Review and deploy `20261004064755_AddSupportTickets` through the existing migration process. It follows the already-present `20261003120351_AddCareerGuidancePaymentLifecycleFoundation` migration. The support migration creates only `SupportTickets` and its indexes, enum/resolution constraints, and restrictive nullable `Users` FK. It has not been applied by this implementation.

## API

All JSON results use the existing `{ "data": ..., "message": ... }` envelope. Ticket numbers are server-generated `CH-` followed by 32 uppercase random GUID characters, backed by an unconditional unique index. Guests cannot retrieve tickets by number; a ticket number is not an access token. Tickets created as guests are not automatically attached to accounts by email.

| Method | Route | Access |
|---|---|---|
| POST | `/api/support/tickets` | Guest or authenticated |
| GET | `/api/support/tickets/my` | Authenticated owner, paginated |
| GET | `/api/support/tickets/my/{ticketNumber}` | Authenticated owner |
| GET | `/api/support/tickets/my/{ticketNumber}/screenshot` | Authenticated owner |
| GET | `/api/admin/support/tickets` | Administrator, paginated/filtered |
| GET | `/api/admin/support/tickets/{id}` | Administrator |
| PATCH | `/api/admin/support/tickets/{id}/status` | Administrator |
| PATCH | `/api/admin/support/tickets/{id}/notes` | Administrator |
| GET | `/api/admin/support/tickets/{id}/screenshot` | Administrator |

Creation accepts multipart fields `Name`, `Email`, `Category`, `Subject`, `Description`, and optional `Screenshot`. Category accepts an enum name or defined numeric value: Login=1, Registration=2, Account=3, Payment=4, Membership=5, JobApplication=6, Referral=7, InterviewInsights=8, TechnicalIssue=9, Other=10. Name/email are required for guests; authenticated requests use the stored account name/email. No `UserId`, ticket number, status, or admin notes are accepted from the creation form.

The response is HTTP 201 with `data.ticketNumber` and message `Your support request has been submitted successfully.` Screenshots accept JPG/JPEG, PNG, or WebP, up to 5 MiB; extension, declared MIME, signature and image structure must agree. The 6 MiB request ceiling allows multipart overhead. Storage keys use random server filenames, and files are served only as protected downloads with private/no-store and nosniff headers. There is no public static screenshot URL. DTOs report `hasScreenshot`; use the matching protected download route.

Creation is limited to five attempts per IP per 15 minutes, with no queue, through the existing rate limiter. Preserve the existing trusted reverse-proxy forwarding configuration; this policy uses `RemoteIpAddress`, not caller-supplied headers.

Lists accept `PageNumber` (default 1, maximum 1,000,000) and `PageSize` (default 20, maximum 100). Admin lists additionally accept `Status`, `Category`, exact case-insensitive `Email`, exact case-insensitive `TicketNumber`, `FromUtc` (inclusive) and `ToUtc` (exclusive). Supply ISO-8601 timestamps ending in `Z`; non-UTC or inverted intervals are rejected. Paging and filters execute in the database.

Status PATCH accepts `{ "status": "InProgress" }`; supported values are Open, InProgress, Resolved, Closed. Entering Resolved sets `resolvedAtUtc`; repeated Resolved requests retain the timestamp. Moving to any other status clears it. Notes PATCH accepts `{ "adminNotes": "Internal text" }` or null to clear. Internal notes and storage keys never appear in owner responses. Revision-based optimistic concurrency prevents overlapping admin updates from silently overwriting one another; conflicts use the existing HTTP 409 handler.

## Email and operational limits

The database save precedes both email notifications. Support receives ticket number, category, submitter name/email, guest/authenticated classification, subject, description and UTC creation time. The submitter receives an acknowledgement with their ticket number. Both use the existing plain-text `IEmailService.SendNotificationAsync`/Brevo adapter; no support inbox rows are created. Uploaded files are not emailed.

Email is best-effort with a bounded timeout and safe logs containing ticket ID/audience only. This first version does not add durable email retry or claim exactly-once delivery. Failed persistence removes the uploaded screenshot; failed uploads remove partial files. As with the existing local file-storage architecture, a process termination between storing a file and saving its ticket can leave an unreferenced file for operational cleanup.

Use the existing Swagger page in Development to submit multipart requests, without a token for guests or with a bearer token for account association. Focused tests generate the Swagger document offline and verify the binary optional screenshot field and optional authentication. Tests also inspect the migration's operations and SQL without connecting to a database.

## Files and validation

Created:

- `JobPortal.API/Controllers/SupportTicketsController.cs`
- `JobPortal.API/Controllers/AdminSupportTicketsController.cs`
- `JobPortal.API/Swagger/SupportTicketsOperationFilter.cs`
- `JobPortal.Application/Features/Support/SupportContracts.cs`
- `JobPortal.Application/Features/Support/SupportValidators.cs`
- `JobPortal.Application/Features/Support/SupportScreenshotValidation.cs`
- `JobPortal.Application/Features/Support/SupportTicketService.cs`
- `JobPortal.Domain/Entities/SupportTicket.cs`
- `JobPortal.Domain/Enums/SupportEnums.cs`
- `JobPortal.Infrastructure/Storage/LocalSupportScreenshotStorage.cs`
- `JobPortal.Persistence/Configurations/SupportTicketConfiguration.cs`
- `JobPortal.Persistence/Repositories/SupportTicketRepository.cs`
- `JobPortal.Persistence.Postgres/Migrations/20261004064755_AddSupportTickets.cs`
- `JobPortal.Persistence.Postgres/Migrations/20261004064755_AddSupportTickets.Designer.cs`
- `JobPortal.Application.Tests/SupportTicketTests.cs`
- `JobPortal.Application.Tests/SupportTicketMigrationTests.cs`
- `docs/support-tickets.md`

Modified:

- `JobPortal.API/Program.cs`: rate-limit policy and Swagger operation filter.
- `JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs`: support service registration.
- `JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`: settings validation/binding and screenshot storage registration.
- `JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs`: repository registration.
- `JobPortal.Persistence/Context/JobPortalDbContext.cs`: SupportTickets DbSet; existing local changes preserved.
- `JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs`: generated support entity; existing local model changes preserved.

Validation on 2026-10-04: 41 support tests plus 143 Candidate/Membership/Brevo/notification-realtime regression tests passed (184 total, zero failures/skips). The Release API build passed with zero warnings/errors. The complete Release solution compiled with 147 existing test analyzer warnings allowed; strict warnings-as-errors build remains blocked by those existing findings. EF reports no pending model changes. Swagger, nullable ownership, unique numbering, private upload/download, state timestamps, concurrency, migration scope and generated SQL were verified offline. The migration was not applied.
