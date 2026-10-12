# Company logo management

## Existing architecture

`Company` already has nullable `WebsiteUrl` and `LogoUrl` (maximum 2048 characters).
`Job.CompanyId` and `JobSource.CompanyId` reference the canonical company. There is no
job-level logo field. No schema change or migration is required.

Public summaries and details expose the existing flat `companyId`, `companyName`,
`companySlug`, `companyLogoUrl` contract. Candidate saved jobs, applications, dashboard,
referrals and company cards also use the company relationship. Null stays null.
Public queries only read stored data and never call the resolver.
Existing public output caching expires after 30 seconds; an already cached response
can briefly show the previous logo after an admin update.

Admin company creation and updates already accept `websiteUrl` and `logoUrl`. The
existing update endpoint validates, audits and saves these fields, including clearing
a logo with null. Source responses already supply `companyId`, `companyName` and
`careerPageUrl`; frontend code can load website and logo using the existing company
GET endpoint. No source contract or ingestion implementation was changed.

## Preview and save

`POST /api/admin/companies/{id}/logo/resolve` requires the existing Administrator role.
Its optional body fields are `websiteUrl`, `careerPageUrl`, `replaceExisting` (false).
The response uses the standard `ApiResponse` envelope:

```json
{
  "data": {
    "companyId": "...",
    "companyName": "Kyndryl",
    "websiteUrl": "https://www.kyndryl.com/",
    "logoUrl": "https://www.kyndryl.com/assets/logo.svg",
    "source": "OfficialWebsiteMetadata",
    "resolved": true
  }
}
```

The URL above illustrates the contract; it is not a verified Kyndryl asset.
An omitted website uses the stored company website. An existing nonempty logo is
returned as `ExistingCompanyLogo` with zero outbound requests. Explicit
`replaceExisting: true` previews another candidate but still does not save it.
No result, unsafe input, unsupported content, HTTP errors, size-limit violations and
discovery timeouts return HTTP 200 with `logoUrl: null`, `source: None`, `resolved: false`.
Caller cancellation propagates. A missing company follows the existing 404 behavior.

After visual review, use **existing** `PUT /api/admin/companies/{id}` with the current
full company update body and selected `logoUrl`; set `logoUrl` to null to clear it.
Persist `websiteUrl` there as well if desired. This is a full update, not a PATCH:
retain other company fields. There is deliberately no duplicate logo save endpoint.
No resolver path saves data or holds a database transaction during HTTP requests.

## Discovery and security policy

- Official website first; career page is considered only when no website is available.
- Workday, Greenhouse, Lever and Ashby hosted domains are excluded from both roles.
  No domain is guessed from a company name or ATS identifier.
- Explicit Organization/Corporation JSON-LD `logo` (string or ImageObject `url`)
  precedes `icon`, `shortcut icon`, and `apple-touch-icon` links. Relative URLs resolve
  against the final page URL. Generic Open Graph/social images are intentionally not
  accepted because they commonly represent content rather than company identity.
- Candidates and redirects remain on the same scheme and host. Cross-origin CDNs,
  www/canonical-host redirects and HTTPS upgrades are conservatively skipped; an
  administrator can supply the final official URL or manually select a CDN logo.
- Only HTTP/HTTPS, default ports, no credentials, maximum URL length 2048. Reject
  single-label and known internal suffixes, loopback, private, link-local, multicast,
  reserved/documentation, carrier NAT and IPv6 local/transition addresses.
- The dedicated factory client disables proxy, cookies and automatic redirects.
  Every redirect is checked. Its connection callback reuses the existing outbound
  public-IP policy, rejects a DNS answer containing *any* nonpublic address, then
  connects directly to a vetted IP. TLS hostname/certificate validation remains
  enabled. This closes DNS-rebinding gaps in preflight-only checks.
- At most three redirects per fetch, eight distinct image candidates, no URL fetched
  twice in one preview. Overall budget 12 seconds; HTTP timeout five seconds. HTML
  limit 512 KiB; image limit 2 MiB, enforced while streaming even without Content-Length.
  Response-header limit 32 KiB. Cancellation is passed through DNS, connect and reads.
- Accept HTML/XHTML for metadata and a fixed image MIME allowlist plus matching
  file signatures for PNG/JPEG/GIF/WebP/ICO; PNG dimensions must be nonzero. SVG must
  begin with an SVG root (allowing XML declaration/comments). SVG is retained solely
  as an external URL: no XML entity resolution, rendering, execution or proxy upload.
  Consumers should display it using an image element, never inject it as HTML.
- Regular expressions have execution time limits; JSON nesting is bounded. No search
  engines, logo vendors, invented favicon paths or live production requests are used.

External URLs are references, not immutable hosted assets: the company can later
move/change them. Resolution verifies content at preview time only. Visual admin
confirmation is necessary to establish brand suitability.

## Company identity audit

Configured ATS providers use the source's CompanyId; ingestion assigns the supplied
company identity to jobs. Logo discovery is never part of ingestion. Existing
publication policy can apply explicitly configured licensed logo metadata only to an
empty company logo; that behavior is preserved.

Company creation checks normalized names and unique slugs. Normalization does not
merge business aliases such as Kyndryl, Kyndryl Inc. and Kyndryl India, so separately
created aliases can remain separate companies with separate logos. Report/resolve
those through company administration; no name-based job matching or normalization
redesign was introduced here.

## Validation

All HTTP tests use synthetic handlers and isolated fixture data; no live ingestion
runs, production requests, deployment, push, migrations or production data changes.

`dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore` passed on
the final production source with **zero warnings and zero errors**. Evidence:
`company-logo-release.log` (ignored local build log).

The normal strict test command failed at compilation on pre-existing analyzer debt
in unrelated test files (including CA1707, CA1861 and CA1859). This failure is kept
in `company-logo-strict-tests.log`; no repository warning/analyzer configuration was
changed. No diagnostics in the new logo test file were reported by this run.

For behavioral validation, tests were run with the invocation-only
`-p:TreatWarningsAsErrors=false`; warnings remain visible and failed assertions still
fail the run. The broader filter covered CompanyLogoTests, AdminCompanyCreationTests,
AdminManagementTests, PublicJobSearchTests, CandidateModuleTests, MultiCompanyProviderTests,
JobAggregationDependencyInjectionTests, SuccessFactorsJobSourceProviderTests and
CompanySourceExpansionRegistrationTests. **324 passed, 4 failed, 328 total, no skips**.
All 42 logo cases in that run passed. It preceded the final SVG-fragment correction.

The four failures are the existing
`SuccessFactorsJobSourceProviderTests.WrongBoardOrOffsetPaginationCannotProduceCompleteSnapshot`
theory cases at line 169: wrong host, wrong board, offset 25, extra path suffix.
Each expected `InvalidDataException`, but no exception was thrown. Both the provider
and the tests are unchanged from HEAD; these cases construct the provider directly
and do not involve company logo registration or discovery. No ingestion behavior or
test expectation was modified to hide these failures. Evidence:
`company-logo-verified-tests.log` and workspace report `company-logo-verified.trx`.

Final focused logo validation includes the SVG-fragment correction: **43 passed,
zero failed, zero skipped**, in 17.16 seconds. Command:

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-restore -p:TreatWarningsAsErrors=false --filter FullyQualifiedName~CompanyLogoTests --logger 'trx;LogFileName=company-logo-final.trx' --logger 'console;verbosity=normal'
```

Evidence: `company-logo-final-tests.log` and workspace report `company-logo-final.trx`.
There were no new logo-file warnings in the final run. Final `git diff --check` passed.
Approval to push is **not recommended** while the
existing provider failures and strict test-project analyzer failures remain unresolved.
