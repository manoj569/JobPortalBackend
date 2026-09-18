# Referral Feature — Files Changed

## New files
- JobPortal.Domain/Entities/JobReferral.cs
- JobPortal.Domain/Entities/ReferralUnlock.cs
- JobPortal.Application/Features/Referrals/ReferralDtos.cs
- JobPortal.Application/Abstractions/Referrals/IJobReferralService.cs
- JobPortal.Application/Abstractions/Referrals/IJobUrlExtractionService.cs
- JobPortal.Application/Features/Referrals/JobReferralService.cs
- JobPortal.Persistence/Repositories/JobReferralRepository.cs
- JobPortal.Infrastructure/Services/ClaudeJobUrlExtractionService.cs
- JobPortal.Shared/Options/AiExtractionOptions.cs
- JobPortal.API/Controllers/ReferralsController.cs

## Modified files
- JobPortal.Domain/Enums/DomainEnums.cs — added `JobReferralApprovalStatus`
- JobPortal.Domain/Entities/Job.cs — added `Referral` nav property
- JobPortal.Domain/Entities/User.cs — added `ReferredJobs`, `ReferralUnlocks` nav properties
- JobPortal.Persistence/Configurations/EntityConfigurations.cs — added `JobReferralConfiguration`, `ReferralUnlockConfiguration`
- JobPortal.Persistence/Context/JobPortalDbContext.cs — added `JobReferrals`, `ReferralUnlocks` DbSets
- JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs — registered `IJobReferralRepository`
- JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs — registered `IJobReferralService`
- JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs — registered `IJobUrlExtractionService`, 2 named HttpClients, `AiExtractionOptions` binding
- JobPortal.API/appsettings.json — added `AiExtraction` config section

## Still to do (not yet in this zip)
1. Generate the EF Core migration locally (SDK isn't available in this sandbox):
   ```
   cd JobPortal.Persistence
   dotnet ef migrations add AddJobReferrals --startup-project ../JobPortal.API
   dotnet ef database update --startup-project ../JobPortal.API
   ```
2. Set a real Anthropic API key: `AiExtraction:ApiKey` in `appsettings.Development.json` or user-secrets.
3. Unit tests (JobPortal.Application.Tests) — not yet written.
4. Frontend screens — not yet built (mockups only, shared earlier in chat).

## Endpoints added
- `POST /api/referrals/extract-from-url` — AI pre-fill from a pasted job URL
- `POST /api/referrals` — referrer submits (creates job as Draft + attaches referral)
- `GET /api/referrals/mine` — referrer's own submissions
- `GET /api/referrals/pending` — admin queue
- `POST /api/referrals/{id}/review` — admin approve/reject (approve publishes the job)
- `GET /api/referrals/unlock/{jobId}` — seeker unlock, gated by active Membership
