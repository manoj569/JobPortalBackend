# NVIDIA source verification — 2026-10-11

Status: **Verified supported Workday board; local inactive request template prepared. Persistent registration and duplicate checks pending authorized inventory access and rights review.** Other company investigations are paused at the user's request.

- Official careers: https://www.nvidia.com/en-us/about-nvidia/careers/
- Official current jobs front end: https://jobs.nvidia.com/careers (linked by official careers Open Positions).
- Front end: Eightfold, observed in official page configuration; backing ATS: Workday.
- Ownership chain: official page https://jobs.nvidia.com/careers/job/893397462894 returns HTTP 200 and embeds Candidate Home URL https://nvidia.wd5.myworkdayjobs.com/NVIDIAExternalCareerSite/login. Evidence: `nvidia-source-verification.json`. This is a board ownership link, not an application submission.
- Workday public board: https://nvidia.wd5.myworkdayjobs.com/NVIDIAExternalCareerSite
- Tenant: `nvidia`; site: `NVIDIAExternalCareerSite`; host: `nvidia.wd5.myworkdayjobs.com`.
- Listing: POST https://nvidia.wd5.myworkdayjobs.com/wday/cxs/nvidia/NVIDIAExternalCareerSite/jobs with `{"appliedFacets":{},"limit":5,"offset":0,"searchText":"India"}` returned five public postings and advertised 273 keyword matches. This is **not** a confirmed total of India jobs or global vacancies.
- Exact detail: GET https://nvidia.wd5.myworkdayjobs.com/wday/cxs/nvidia/NVIDIAExternalCareerSite/job/India-Bengaluru/Senior-Manager--Inception---South-Asia-Region_JR2024311
- Detail repeated successfully: requisition `JR2024311`, current title `Head of Startups - India and South Asia`, location `India, Bengaluru`, country `India`, `canApply=true`, no end date. External path slug and current title differ; use returned requisition ID, not title/slug, as identity.
- Returned application entry: https://nvidia.wd5.myworkdayjobs.com/NVIDIAExternalCareerSite/job/India-Bengaluru/Senior-Manager--Inception---South-Asia-Region_JR2024311
- Repeat detail evidence: `nvidia-source-detail-verification.json`; first listing/detail: `company-source-expansion-probes.json`.

Reuse `AtsType.Workday` (5) and existing validation/adapter. No Eightfold adapter is needed for this verified backing board. Registration request template is in `company-source-expansion-ready.json`: careerPageUrl exact public board above, atsIdentifier `nvidia/NVIDIAExternalCareerSite`, isActive=false, scanIntervalMinutes=720, companyId=null awaiting the real existing company lookup. The catalog is not loaded by the application and creates no persistent records.

Repository documentation/configuration searches did not identify an existing NVIDIA source; absence in repository is not absence in the production inventory. The production backend is https://job-portal-lgjc.onrender.com, with Administrator-protected GET `/api/admin/job-sources`. No authorized admin session was supplied. Do not connect directly to Neon or bypass authentication. Resolve all company/source inventory pages, aliases and canonical board identity before any registration. Local PostgreSQL is not a requirement. Public access does not establish redistribution rights; preserve existing source approvals, licensing, India eligibility and optional category rules.

Paging supports the observed limit/offset contract, but only offset=0 was fetched. Full pagination completeness, scan coverage and reconciliation readiness are unverified. No full scan, importer, application submission, database write, deployment, migration or schedule enablement occurred. Native official careers fetch timed out once; the web rendering supplied the Open Positions link. The unauthenticated production inventory GET also timed out after 20 seconds; no HTTP status, registry contents or authenticated access was established. No restriction was bypassed.

Validation: an earlier normal Release backend build passed with zero warnings/errors; existing registration/administration regression tests passed 29/29. Compilation of the new registration fixtures is currently blocked by concurrent Workday hardening: `WorkdayJobSourceProvider.cs` references `IJobSourceRunCheckpoint.Details`, which the interface at that build snapshot lacked (CS1061). The new six-company fixture tests have **not passed or executed**. That protected implementation is untouched. No test result is claimed for new persistent registration or sample ingestion.
