# Interview Insight Premium Access Change

## Previous access decision tree

`GET /api/candidate/interview-insights/{id}` authenticates a Candidate, loads the insight, and returns 404 if it is missing/deleted. An insight author can read their own insight; other candidates can only retrieve published insights. For a published insight, the service sets `CanReadFull` when the reader is the author, has an application at the insight's company, or has a non-cancelled interview schedule there. Otherwise, it returns the response with preparation tips and rounds redacted. The existing company eligibility checks run after the publication/ownership check.

The list endpoint already returns published insight cards to authenticated Candidates without a membership requirement. `CanReadFull` on a card represented company-specific application/schedule access. Contributions, scheduling, and other Candidate Insight actions are not gated on membership by the current Candidate controller.

## New access decision tree

1. The Candidate controller's existing `[Authorize(Roles = "Candidate")]` and service-side active Candidate check remain in force.
2. The service loads the insight through the existing repository query filter, which excludes soft-deleted rows.
3. The existing author/published visibility rule runs unchanged. Premium does not make another user's unpublished insight visible.
4. Full-detail access is granted to the author, an authenticated candidate with an active Premium membership, or a candidate who satisfies the existing company application/schedule rule.
5. If none applies, `CanReadFull` remains false and protected preparation tips and rounds remain redacted.

## Premium source of truth

Premium is the existing `CareerHarborMembership` plan code used by `CandidateService` to expose the candidate's `IsPremium` and Premium application quota. The detail service uses `IMembershipRepository.GetActiveForUserAsync(candidateId, "CareerHarborMembership", ct)`. That repository query uses the authenticated Candidate ID passed by the controller and requires `Status == Active`, `StartsAtUtc <= now`, and no end date or `EndsAtUtc > now`; it is async, `AsNoTracking`, and respects the membership soft-delete query filter. No payment/client-provided value or plan name grants access.

## Endpoint and response contract

- Detail endpoint: `GET /api/candidate/interview-insights/{id}`
- The existing response contract is unchanged. The frontend should use `data.canReadFull`; when true, render the returned `preparationTips` and `rounds`. When false, keep the existing locked message. Locked responses continue to set `preparationTips` to `null` and `rounds` to an empty collection.
- List endpoint: `GET /api/candidate/interview-insights`; its existing card `canReadFull` now also reflects active Premium access. Premium entitlement is resolved once per list request, not once per insight.
- Company summary: `GET /api/candidate/interview-insights/company/{companyId}/summary`; its existing `canReadFull` also reflects Premium entitlement.
- No response fields were added, renamed, or removed. No `PREMIUM_MEMBERSHIP` reason is needed for this Boolean contract.
- Authentication remains Candidate-role protected. The API derives the candidate ID from `User.GetRequiredUserId()`.

## Files changed

- `JobPortal.Application/Features/InterviewInsights/InterviewInsightServices.cs` — central full-detail decision; active Premium membership check; one membership lookup for listing; company summary access result.
- `JobPortal.Application/Abstractions/InterviewInsights/InterviewInsightContracts.cs` — passes the one-per-request Premium entitlement to the card repository projection.
- `JobPortal.Persistence/Repositories/InterviewInsightRepository.cs` — includes the supplied entitlement in card `CanReadFull` without per-card membership queries.
- `JobPortal.Application.Tests/InterviewInsightsTests.cs` — coverage added for Premium across companies, membership ownership, expired/inactive Premium fallback, publication/deletion restrictions, and existing application unlock.

No frontend, Talentd, referrals, Career Guidance, pricing, or membership duration files were changed.

## Tests and validation

- API build: **passed**, `dotnet build JobPortal.API/JobPortal.API.csproj --no-restore -m:1 -nr:false` (0 warnings, 0 errors).
- Test project build: **passed** with `-p:RunAnalyzers=false` (0 warnings, 0 errors). Without that invocation-only override, the test project build is stopped by existing analyzer-as-error findings across unrelated test files, including CA1707 and CA1861.
- Focused Interview Insight/membership tests: **not executed**. The test assembly compiled, but VSTest reported that it could not connect to the testhost after 90 seconds. A retry with `VSTEST_CONNECTION_TIMEOUT=300` remained at testhost startup and was stopped. No test results are claimed.
- Broader applicable backend tests: **not run**, because the focused test runner could not start its testhost.
- `git diff --check`: **passed**. Git printed existing line-ending normalization warnings for unrelated pre-existing modified files.

## Database and performance

- Migration required: **NO**. Existing membership data and query support the entitlement.
- No schema, database, or migration changes were made.
- Detail retrieval does one indexed membership lookup for a non-owner before falling back to the existing company checks. Owner access short-circuits without that query.
- Insight listing resolves membership once before retrieving cards and uses that Boolean in the existing projection; it does not add N+1 membership queries. Existing application/schedule card checks remain in place. Membership lookups are not added to contributions or other free functionality.

## Frontend handoff

Continue calling `GET /api/candidate/interview-insights/{id}` when the candidate selects **Read Insight**. Trust `data.canReadFull`. An active Premium candidate receives `canReadFull: true` plus full `preparationTips` and `rounds`, including for another company's published insight. A free locked candidate receives `canReadFull: false`, `preparationTips: null`, and `rounds: []`; show the current locked message. No frontend Premium flag or special Premium request parameter is needed.

## Risks and limitations

- The source change and API build are complete, but focused and broader test execution remain unverified because VSTest could not connect to testhost in this environment.
- The test project currently has unrelated analyzer-as-error findings; the test project compiled with analyzers disabled for the verification build only. Project configuration was not changed.
- The active Premium plan code is consistent with the existing candidate Premium/quota implementation (`CareerHarborMembership`).
