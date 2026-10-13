# Browse Jobs production backend audit — 2026-10-13

## A. Root causes

1. Public Browse Jobs binds PublicJobQuery with keyword/search but no q. Requests sending q silently had no text constraint.
2. The existing shared keyword predicate omitted Job.Location and WorkplaceType.Remote. Cross-field queries containing a city could fail even though each other token matched.
3. Recommended sorted featured status and publication date, with no text relevance. Weak description matches could precede title matches.
4. GET /api/candidate/jobs bound CandidatePageQuery (pagination only), and CandidateRepository did not search/filter. Counts correctly described that unfiltered universe but could not describe frontend selections.
5. dotnet/aspnet did not match punctuation variants. Existing whitespace tokenization, company/skill search, publication safeguards, and public filter-before-pagination were already correct.

6. Null/empty sortDirection could throw from the validator instead of returning a validation error. It now returns the existing HTTP 400 validation response.

No React source exists here. Frontend client-only filtering, exact control labels, debounce, request construction and deployed behavior cannot be confirmed. The prior audit is docs/candidate-job-search-filter-audit.md; this report describes the additional changes.

## B. Changed files

- API/Controllers/CandidateController.cs: binds the existing shared public browse query.
- Application/Abstractions/Candidates/CandidateContracts.cs and Abstractions/Persistence/CandidateRepositoryContracts.cs: candidate browse query contract.
- Application/Features/Candidates/CandidateService.cs: validates shared query; reports effective page alias.
- Application/Features/PublicJobs/PublicJobDtos.cs: adds optional Q at end, preserving existing positional construction.
- Application/Features/PublicJobs/JobBrowseQueryPolicy.cs: shared normalization and parameter precedence.
- Application/Features/PublicJobs/PublicJobService.cs: normalization for search and contextual facets.
- Application/Features/PublicJobs/PublicJobValidator.cs: q length, singular enum validation and null-safe sort-direction validation.
- Persistence/Repositories/CandidateJobKeywordQuery.cs: locations, Remote, conservative technology aliases, SQL-translatable relevance.
- Persistence/Repositories/PublicJobRepository.cs: shared sorting and safe structured diagnostics.
- Persistence/Repositories/CandidateRepository.cs: reuse visibility/filter/sort query before candidate exclusion/count/pagination.
- Application.Tests/PublicJobSearchTests.cs: canonical query, ranking, UTC boundaries, composition, candidate pagination/visibility and SQL translation.
- Application.Tests/CandidateModuleTests.cs: test double follows shared query signature.
- Application.Tests/CandidateJobSearchPostgresTests.cs: optional disposable PostgreSQL execution coverage updated for q/ranking/aliases/page two.

Paths above have the JobPortal. prefix under repository root. This report is also added; docs/candidate-job-search-filter-audit.md now points to the current audit.

## C. Existing API contract

GET /api/jobs, /api/jobs/search and /api/jobs/filter -> PublicJobsController -> PublicJobService -> IPublicJobRepository -> PublicJobRepository -> IQueryable/EF Core -> PostgreSQL. keyword precedes search. Default sort Recommended. Public filters already included location(s), company id(s)/name, category id/name, employment type(s), workplace/workModes, enum/numeric experience, freshnessDays, salary/currency/period, department, role category, education, company type, industry, posted-by, internship duration/flexibility and featured status. page overrides pageNumber; pageSize 1–100; bounded offset. Response: items, pageNumber, pageSize, totalCount, totalPages, appliedSort.

GET /api/candidate/jobs -> CandidateController -> CandidateService -> ICandidateRepository -> CandidateRepository -> EF Core -> PostgreSQL. Only pageNumber/pageSize, with candidate authorization and already-applied exclusion. Response: items, pageNumber, pageSize, totalCount, totalPages.

## D. Final API contract

Existing routes remain. Both browse paths now accept PublicJobQuery, including q and all existing public filters. Existing public keyword/search aliases remain. Precedence: first nonblank q, keyword, search. Public and candidate response shapes are unchanged; candidate workMode naming is retained. page overrides pageNumber for both browse paths. Candidate authentication, active-candidate validation and already-applied exclusion remain.

Example: /api/jobs?q=.net&location=Pune&experienceLevel=Senior&freshnessDays=7&pageSize=25
Candidate equivalent: /api/candidate/jobs with the same parameters.

The backend uses freshnessDays, experienceLevel, minExperienceYears/maxExperienceYears and employmentType(s). It does not claim unsupported datePosted/experience UI aliases. Named enums or existing numeric enum values remain accepted.

## E. Search semantics

Trim/collapse whitespace; case-insensitive literal partial terms. Empty search adds no predicate. AND across distinct whitespace tokens, OR across existing searchable fields for each token. Fields: title, description, requirements, responsibilities, benefits, department, role category, education, company name, actual category name, JobSkills.Skill.Name, location. The token remote additionally matches WorkplaceType.Remote. No URLs, source JSON, candidate or referrer details participate. Punctuation stays intact: .NET, ASP.NET, C#, C++, Node.js; %, underscore and backslash remain literal, not wildcards.

Centralized token aliases: dotnet <-> .net, aspnet <-> asp.net. No broad js/ts synonyms. All tokens are required, even when each matches a different field. Full-query matching affects ranking, without bypassing required tokens.

## F. Filter and tab semantics

All filters execute server-side, AND across dimensions; values within arrays are OR. Singular plus array constraints intersect (existing contract). Numeric experience interval filters override legacy enum experience when numeric bounds exist; unknown numeric metadata is excluded when that filter is selected. Salary scoping/policy remains unchanged.

Location input is independent of q and uses trimmed case-insensitive partial text. location=Remote additionally matches Remote workplace metadata. locations array remains exact persisted facet values; use workModes for structured workplace selections.

Date posted uses rolling UTC publication boundaries: freshnessDays=1/3/7/15 for 24 hours/3/7/15 days; omit it for all time (30 remains supported). Lower boundary is inclusive. No fabricated publication dates.

All jobs: omit type/experience constraints. Internships: employmentType=Internship. Fresher: existing experienceLevel=Entry or the existing numeric range selected by the client. Experienced: existing enum or numeric range contract; the repository does not invent a new Experienced enum or guess frontend thresholds. UI source is required to certify those mappings.

Referral route /api/jobs/referrals and referralOnly retain approved referral and active nondeleted referrer rules. Resume matches was not reactivated. Existing recommendations, membership/application quotas and referral request services are unchanged.

Public filter-options/facets retain disjunctive counts: same q and visibility, excluding the facet’s own dimension. They are public counts; candidate browse counts additionally exclude that candidate’s applications. No candidate-specific facet endpoint is introduced. Popular companies remains an intentionally global endpoint.

## G. Ranking

Recommended + search orders by full-query tier: exact trimmed case-folded title, title starts-with, title contains, company contains, skill contains. Next, sum per-token best-field weights: title 4, skill 3, description/requirements/responsibilities 2, location 1. Then featured, publication descending, Id ascending. This is server-side CASE/EXISTS ordering, with no in-memory scoring. Company exact/partial share a tier. Alias-only matches may score below literal matches; this is deliberately lightweight ranking.

Explicit LatestPublished, NewestAdded, ClosingSoon, salary or title sorts retain their existing behavior. Recommended without search retains public featured/date ordering. Candidate browse now adopts this shared Recommended default instead of its former fixed latest ordering. Every sort ends with Id for stable ties. Relevance is descending independently of sortDirection; existing sortDirection handling remains on field-based modes.

## H. Pagination, counts and visibility

Published, nonhidden, nondeleted job, publication timestamp, nondeleted company/category and future-or-null expiry -> q -> filters -> candidate applied-job exclusion where applicable -> CountAsync once -> server ordering -> Skip/Take -> projection. No ToList or client-side filtering precedes pagination. totalCount and totalPages describe all matches, not current-page size. Out-of-range pages are empty while retaining count. Count/page are separate SQL statements and can diverge during concurrent writes (existing isolation behavior).

Source/provider ingestion eligibility and job publication decisions remain unchanged. There is no separate Job.IsActive field; Published/Closed/hidden/expiry represent current candidate visibility. No production data was read or written.

## I. Performance and observability

No N+1 queries or eager Includes; projections join company/category, skills use EXISTS. PostgreSQL SQL translation tests cover ranked CASE, filters, LIMIT/OFFSET. Existing B-tree indexes include CompanyId/Status/PublishedAtUtc, CategoryId/Status, Status/IsFeatured/IsHidden/PublishedAtUtc, Status/ExpiresAtUtc, Status/WorkplaceType/EmploymentType, Status/PostedByType, Department, RoleCategory, CreatedAtUtc, plus JobSkills(JobId, SkillId) and skill normalized name.

Existing lower(column).Contains predicates remain for safe literal semantics and compatibility with supported test providers. Leading-substring search is not served efficiently by ordinary B-tree indexes; this is a correctness fix, not a claim of indexed search at 100k jobs. Term and filter bounds remain validated. Facets retain sequential grouped queries.

Separate measured proposal, not applied: pg_trgm GIN expression indexes on lower(Jobs.Title), lower(Jobs.Description), lower(Jobs.Location), lower(Companies.Name) and lower(Skills.Name), paired with escaped LIKE on those exact expressions if EXPLAIN shows the current translated Contains uses strpos instead of indexable LIKE. This requires pg_trgm, space/write-cost review and production-like measurements. A maintained search document may be preferable as scale grows. No index or extension was added.

Structured diagnostics: query/location length, active filter count, requested sort, effective page, page size, total count, elapsed milliseconds. No raw user search/location text, candidate identifiers or descriptions. Cached public responses may bypass repository diagnostics.

## J–L. Validation

20 new executable cases cover canonical q/title/case/company/description/skill/punctuation/location/multi-word search, UTC date boundaries, composition, public/candidate page two and SQL ranking translation. Existing visibility and pagination validation tests were extended for candidate visibility and invalid sort direction. The optional PostgreSQL test was extended for q, relevance, aliases and page two.

Results and exact commands follow below. InMemory correctness plus SQL translation do not substitute for PostgreSQL execution or live frontend/HTTP validation.

## M. Migration required

No. No schema, index, ingestion/provider, quota or authentication changes.

## N. Remaining risks

Missing frontend source prevents certification of UI tab/control requests. Explicit disposable PostgreSQL configuration is required to execute database tests; production configuration is never substituted. Substring scan cost, alias-only ranking and concurrent count/page changes are described above. Public and candidate result universes intentionally differ for already-applied jobs. Existing 30-second public output cache can delay updates/expiry visibility until expiry of its entry.

## O–P. Working tree

Final git diff --stat and git status --short are recorded below after validation. All work remains unstaged. No commit, push or deployment.

## Exact validation commands and results

- Initial focused/relevant suite: 245 passed, 0 failed, 2 skipped, 247 total; duration 1m58s. Command: `dotnet test JobPortal.Application.Tests --no-restore -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~PublicJobSearchTests|FullyQualifiedName~CandidateJobSearchPostgresTests|FullyQualifiedName~CandidateModuleTests|FullyQualifiedName~SalaryCompatibility|FullyQualifiedName~JobClassification|FullyQualifiedName~Experience' --logger 'trx;LogFileName=browse-focused.trx'`.
- Broader regression suite: 348 passed, 0 failed, 8 skipped, 356 total; duration 2m43s. Command: `dotnet test JobPortal.Application.Tests --no-restore -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~PublicJobSearchTests|FullyQualifiedName~CandidateJobSearchPostgresTests|FullyQualifiedName~CandidateModuleTests|FullyQualifiedName~CandidateRepositoryTests|FullyQualifiedName~PublicPopularCompaniesTests|FullyQualifiedName~SalaryCompatibility|FullyQualifiedName~SalaryPeriod|FullyQualifiedName~JobClassification|FullyQualifiedName~Experience|FullyQualifiedName~Referral' --logger 'trx;LogFileName=browse-regression-final.trx'`. This preceded the final null-safe sort-direction validation adjustment, covered by the final focused run below.
- Final strict production build: `dotnet build JobPortal.API -c Release --no-restore`: PASS, 0 warnings, 0 errors; elapsed 1m13.78s. Evidence: `browse-api-release-final.log`.
- Test compilation used `TreatWarningsAsErrors=false` for existing unrelated analyzer warnings (including CA1707 and xUnit2031). Repository warning policies were not changed. The normal strict production build passed. 
- PostgreSQL execution tests skip because `WORKDAY_CHECKPOINT_TEST_POSTGRES` is absent. The guarded fixture only permits 127.0.0.1 / careerharbor_workday_test; it never uses production configuration. SQL translation tests execute without connecting and passed in the suites above.
- TRX evidence is retained in `C:/Users/Ashish/Documents/Codex/2026-07-27/browse-jobs-test-results`; ignored log files are in the repository root. No test evidence was staged.

- Final Browse Jobs run after the sort validation correction: **77 passed, 0 failed, 1 skipped, 78 total**; duration 2m1s. Command: `dotnet test JobPortal.Application.Tests --no-restore -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~PublicJobSearchTests|FullyQualifiedName~CandidateJobSearchPostgresTests' --results-directory 'C:/Users/Ashish/Documents/Codex/2026-07-27/browse-jobs-test-results' --logger 'trx;LogFileName=browse-focus-final.trx'`. Evidence: `browse-focus-final.log` and `browse-focus-final.trx`.
- Final `git diff --check`: PASS. `git diff --cached --stat`: empty. No commit, push, deployment or migration.

## O. Exact git diff --stat

This command excludes the two new untracked files listed in status.

```text
 JobPortal.API/Controllers/CandidateController.cs   |   3 +-
 .../CandidateJobSearchPostgresTests.cs             |  12 ++-
 .../CandidateModuleTests.cs                        |   3 +-
 .../PublicJobSearchTests.cs                        | 111 +++++++++++++++++++++
 .../Abstractions/Candidates/CandidateContracts.cs  |   3 +-
 .../Persistence/CandidateRepositoryContracts.cs    |   3 +-
 .../Features/Candidates/CandidateService.cs        |  12 ++-
 .../Features/PublicJobs/PublicJobDtos.cs           |   2 +-
 .../Features/PublicJobs/PublicJobService.cs        |   4 +-
 .../Features/PublicJobs/PublicJobValidator.cs      |   8 +-
 .../Repositories/CandidateJobKeywordQuery.cs       |  50 +++++++++-
 .../Repositories/CandidateRepository.cs            |  24 +++--
 .../Repositories/PublicJobRepository.cs            |  49 +++++++--
 docs/candidate-job-search-filter-audit.md          |   2 +
 14 files changed, 252 insertions(+), 34 deletions(-)
```

## P. Exact git status --short

```text
 M JobPortal.API/Controllers/CandidateController.cs
 M JobPortal.Application.Tests/CandidateJobSearchPostgresTests.cs
 M JobPortal.Application.Tests/CandidateModuleTests.cs
 M JobPortal.Application.Tests/PublicJobSearchTests.cs
 M JobPortal.Application/Abstractions/Candidates/CandidateContracts.cs
 M JobPortal.Application/Abstractions/Persistence/CandidateRepositoryContracts.cs
 M JobPortal.Application/Features/Candidates/CandidateService.cs
 M JobPortal.Application/Features/PublicJobs/PublicJobDtos.cs
 M JobPortal.Application/Features/PublicJobs/PublicJobService.cs
 M JobPortal.Application/Features/PublicJobs/PublicJobValidator.cs
 M JobPortal.Persistence/Repositories/CandidateJobKeywordQuery.cs
 M JobPortal.Persistence/Repositories/CandidateRepository.cs
 M JobPortal.Persistence/Repositories/PublicJobRepository.cs
 M docs/candidate-job-search-filter-audit.md
?? JobPortal.Application/Features/PublicJobs/JobBrowseQueryPolicy.cs
?? docs/browse-jobs-production-audit.md
```
