# Candidate job search and filter audit

## Endpoint and root cause

`GET /api/jobs`, `/api/jobs/search`, and `/api/jobs/filter` use the same stateless controller action and PublicJobQuery. Live typing, Enter, and Search Jobs must send the current query to this contract. Existing output caching varies by all query parameters.

The old repository already included Description, Title, Company.Name, Category.Name and skill names. A title-only defect is NOT confirmed. Its `Contains(keyword)` was case-sensitive on PostgreSQL, required the entire phrase in one field, and omitted Requirements/Responsibilities and structured content. This explains case differences and missing content matches; frontend request construction cannot be verified because React source is absent.

## Keyword contract

Keyword takes precedence over the legacy search alias when nonblank. Split on whitespace, lowercase terms invariantly, deduplicate, AND every term; each term may match a different field. Partial literal matching preserves `.NET`, `C#`, `C++`, `ASP.NET`, `Node.js`, percent, underscore and backslash. Blank input means no keyword constraint. No dot-net synonym expansion or HTML stripping is introduced.

Searchable fields: Title, Description, Requirements, Responsibilities, Benefits, Department, RoleCategory, EducationRequirement, Company.Name, actual Category.Name, JobSkills.Skill.Name. Null optional fields do not match. The old synthetic category label `Not specified` no longer participates. URLs, source/provider JSON, classification diagnostics, hashes, identifiers, personal/referrer data, and company description are excluded.

`CandidateJobKeywordQuery` builds IQueryable predicates shared by search and facets through ApplyFilters. EF/Npgsql translates lowercase Contains to literal substring SQL (strpos, or escaped LIKE depending on translation shape), with parameterized terms. No manual LIKE pattern is built; user wildcards cannot broaden the query. No jobs are materialized before filtering.

## Exact API contract

Frontend names below are the existing backend binding names expected from React, not observations of unavailable frontend code. Arrays use repeated query keys. Enums retain existing named/numeric binding.

| Frontend parameter | Backend parameter | Applied DB field / behavior | Status |
|---|---|---|---|
| keyword / search | Keyword / Search | Content fields above; keyword wins | PASS |
| location | Location | Location, trimmed case-insensitive partial text | PASS |
| locations | Locations | Location, trimmed lowercase exact facet values, OR | PASS |
| employmentType / employmentTypes | EmploymentType / EmploymentTypes | EmploymentType | PASS |
| workplaceType / workModes | WorkplaceType / WorkModes | WorkplaceType | PASS |
| minExperienceYears / maxExperienceYears | MinExperienceYears / MaxExperienceYears | Numeric experience interval | PASS |
| experienceLevel | ExperienceLevel | Legacy enum only when no numeric bounds supplied | PASS |
| minimumSalary / maximumSalary | MinimumSalary / MaximumSalary | Salary interval, legacy aliases | PASS |
| minAmount / maxAmount | MinAmount / MaxAmount | Salary interval, take precedence over aliases | PASS |
| currencyCode / salaryPeriod | CurrencyCode / SalaryPeriod | Compatible currency and period only | PASS |
| freshnessDays | FreshnessDays | PublishedAtUtc >= clock minus days | PASS |
| departments | Departments | Department exact persisted options | PASS |
| roleCategories | RoleCategories | RoleCategory exact persisted options | PASS |
| educationRequirements | EducationRequirements | EducationRequirement exact persisted options | PASS |
| companyId / companyIds / companyName | CompanyId / CompanyIds / CompanyName | CompanyId and partial case-insensitive Company.Name | PASS |
| categoryId / categoryName | CategoryId / CategoryName | CategoryId and partial case-insensitive Category.Name | PASS |
| companyTypes / industries | CompanyTypes / Industries | Company.CompanyType / Industry | PASS |
| postedByTypes | PostedByTypes | PostedByType | PASS |
| internshipDurationMonths / flexibleDuration | InternshipDurationMonths / FlexibleDuration | Duration metadata | PASS |
| isFeatured / featuredOnly | IsFeatured / FeaturedOnly | IsFeatured; featuredOnly takes precedence | PASS |
| referralOnly | ReferralOnly | Approved referral, active nondeleted referrer | PASS |
| sortBy / sortDirection | SortBy / SortDirection | Existing PublicJobSort modes | PASS |
| pageNumber / page / pageSize | PageNumber / Page / PageSize | Page overrides pageNumber; bounded validated pagination | PASS |

Filters AND across dimensions, OR within arrays. Singular and array filters supplied together intersect. Text option filters other than location retain exact stored-value matching; send facet values. Unknown metadata remains visible without its filter; explicit metadata filters exclude unknown values.

## Experience, salary and location

Experience uses inclusive interval overlap, structured numeric bounds only. Minimum-only job [3, infinity] overlaps candidate [2,4], without manufacturing a maximum. Existing maximum-only behavior treats the sole known value as the lower comparison point too (preserved compatibility). Both unknown bounds are excluded only when a numeric filter is active. Numeric filters override stale ExperienceLevel. Education text is never parsed into work experience. Any valid caller interval is supported, including 10+ as minExperienceYears=10 with no maximum; the existing experience-ranges endpoint bins are unchanged.

Salary retains SalaryQueryPolicy: legacy amount filters and salary sort/facet default the REQUEST scope to INR/Yearly. Missing job currency/period is never inferred. Explicit alternative scopes require both valid currency and period for salary comparisons. No conversions. Bounds use inclusive overlap with single-known-bound fallback. Salary facet retains currency/period and drops amount bounds. Unknown salary is excluded by an amount constraint; known scope but null amounts can still be present in salary sorting, preserving existing behavior.

Search-bar location is literal partial matching; structured locations are exact case-folded facet values. They intersect when both are supplied. No search-time country inference or city synonym expansion. Existing ingestion normalization stays unchanged; users should select canonical facet values (e.g. Bengaluru when stored that way). Remote matches a Location string through location input; use workModes=Remote for workplace metadata.

## Visibility, pagination, sorting and facets

Base query preserves Published status, publication timestamp, nonhidden/nondeleted job, nondeleted company/category, and absent-or-future expiry. Rejected/draft/closed records remain excluded. Keyword and filters precede Count, then sorting, Skip, Take, projection. Service reports filtered TotalCount/TotalPages and requested effective page/pageSize. Every supported sorting branch has Id as a final tie-breaker. Recommended, LatestPublished, NewestAdded, ClosingSoon, SalaryHighToLow, Title, MinimumSalary and MaximumSalary remain supported; sortDirection controls the existing field-based modes only.

`/api/jobs/filter-options` and `/api/jobs/facets` are aliases. All facet dimensions use the same keyword and base visibility, dropping only their own dimension (disjunctive counts), so their summed counts need not equal filtered page totals. Location facets drop both location inputs; company facets drop company id/name filters; experience facets drop numeric bounds. Legacy experienceLevel remains a separate constraint when numeric bounds are absent. Numeric range facets report stored extrema, not invented requirements. No separate supplemental candidate count endpoint was found; the counts in this response are the supported supplemental counts. Popular companies is an intentionally global endpoint with only limit, not contextual facets.

## Performance and regression verification

Lowercase substring predicates may scan rows and each term adds predicates plus a skill EXISTS. Keyword length is capped at 250; pageSize at 100. Facets execute sequential grouped SQL queries using the existing architecture. At larger scale consider measured PostgreSQL trigram expression indexes or a maintained search document/full-text strategy; no migrations or infrastructure changes are required here. No candidate data or full job descriptions are logged.

Added regression cases cover title/description-only .NET, case/trim/partial .NET, Requirements, Responsibilities, Department, RoleCategory, Benefits, EducationRequirement, C#, C++, Node.js, SQL Server, percent/underscore/backslash literals; cross-field AND, keyword alias, missing term, blank keyword, combined location/work-mode/experience, stale enum precedence, matching hidden/deleted/draft/expired jobs, a previously later job on filtered page 1, count/pages, contextual facets and repeat ordering. Npgsql ToQueryString tests verify parameterized server translation. Existing public search tests cover every typed filter, null experience, range overlap, sorting and facets; salary compatibility and classification suites cover unknown/incompatible salary and structured metadata.

Execution results are recorded below after local validation. InMemory regressions do not substitute for PostgreSQL execution or a deployed HTTP/cache test. No frontend code was changed; no required parameter rename. Both frontend triggers should submit the same current query; facet requests should include that query. React debounce/cancellation and stale-response handling remain frontend responsibilities.

No push, deployment, production access, migration, backfill or provider-specific search changes.

## Local validation results

- Final relevant suite: **156 passed, 0 failed, 2 skipped**, 158 total. Includes PublicJobSearchTests, JobClassificationTests, SalaryCompatibility tests and the new CandidateJobSearchPostgresTests. Command: `dotnet test JobPortal.Application.Tests --no-restore -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~PublicJobSearchTests|FullyQualifiedName~CandidateJobSearchPostgresTests|FullyQualifiedName~SalaryCompatibility|FullyQualifiedName~JobClassification|FullyQualifiedName~SalaryPeriod|FullyQualifiedName~Experience' --logger 'trx;LogFileName=candidate-search-final.trx'`.
- PostgreSQL search and salary execution tests skipped: WORKDAY_CHECKPOINT_TEST_POSTGRES is absent. The reused fixture guards host 127.0.0.1 and database careerharbor_workday_test; it never falls back to production configuration. Npgsql SQL translation tests passed without opening a connection.
- Normal warnings-as-errors test build is blocked by existing analyzer violations in unrelated test files, including CA1707, CA1861, CA1869 and xUnit2031. No unrelated tests or project warning policies were edited. No new test runtime failures. Initial sandbox attempts also encountered SDK access restrictions; authorized local runs outside the sandbox succeeded.
- `dotnet build JobPortal.API -c Release --no-restore`: **PASS**, zero warnings and zero errors with normal repository settings. This is the deployable API build, not a claim that the entire solution's strict test build passes.
- Scoped `git diff --check`: PASS.
- Evidence logs in repository root (ignored *.log): candidate-search-final.log, candidate-search-release.log. TRX evidence is retained in the parent workspace as candidate-search-validation.trx and candidate-search-initial.trx.
- Remaining validation risks: PostgreSQL execution, deployed HTTP/output-cache behavior, and actual React request construction were not exercised. Generic provider handling needs no provider branch because the repository searches persisted shared Job fields. Substring scans may need measured indexing at larger scale. Existing unrelated Workday edits remain in the checkout and are outside this change.
