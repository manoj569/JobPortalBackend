# SuccessFactors job sources

`AtsType.SuccessFactors` (`4`, the existing numeric JSON/database value) resolves
only to `SuccessFactorsJobSourceProvider`. Greenhouse, Lever and Ashby keep their
own providers; Custom does not fall back to SuccessFactors.

Configure each source through the existing administrator create/edit endpoints:

- `CompanyId`: the existing company owning the imported jobs.
- `CareerPageUrl`: the official public HTTPS board URL, shaped as
  `https://<company-careers-host>/go/<board-slug>/<numeric-board-id>`.
- `AtsIdentifier`: the exact numeric board ID from the URL (not a company name,
  hostname or full URL).
- `ScanIntervalMinutes`: the existing scheduling interval; unchanged by the provider.
- Category: the existing configuration-managed
  `JobAggregation__SourceCategories__<source-guid>` mapping; not an admin DTO field.

The existing Deloitte India URL
`https://southasiacareers.deloitte.com/go/Deloitte-India/718244` and identifier
`718244` continue to work unchanged. No schema migration or source-data backfill
is required. Do not deploy/apply changes as part of local validation.

## Supported template and safety limits

This is a reusable reader for the server-rendered template already supported by
the Deloitte implementation, not every SuccessFactors tenant/template/language.
It requires English `Results ... to ... of ...` metadata, `data-row` listing rows,
`paginationItemLast` links, public `/job/` detail links and `itemprop` detail fields.
An unsupported/restricted/CAPTCHA page fails closed; no access restrictions are bypassed.
Validate a new company's fixtures before enabling its source.

Only HTTPS on port 443 is supported. Credentials, IP-literal/local hosts,
source query strings/fragments, arbitrary paths and mismatched board IDs are
rejected. Pagination must stay on the exact configured board and origin;
details must stay on that origin and under `/job/`. Redirects are not followed.

The named HTTP client resolves DNS at connection time, rejects the whole result
if any address is private/reserved, and connects directly to one of those vetted
addresses. This prevents DNS rebinding to internal services. Proxies are disabled
on this client; normal TLS hostname/certificate validation remains enabled.
Public-host configuration is administrator-controlled, not a general URL-fetch API.

The existing retry handler, 250 ms pacing, maximum two concurrent detail requests,
25-job pages, 10,000-job scan limit and 4 MiB page limit are retained. Company name
comes from the configured company, never a provider-specific default.

Stable requisition IDs (or numeric public posting IDs as fallback) still feed
the unchanged `JobSourceId + ExternalJobId` ownership/dedup path. Missing jobs
are closed only after a complete scan with no skipped/failed items and complete
unique identities. Manual jobs are never adopted or reconciled. Failed pagination,
duplicate links/IDs, malformed details or HTTP failures cannot close stale jobs.

Provider/runner scan log labels are now `SuccessFactorsSync...`, not Deloitte-specific.
Automated tests use mocked HTTP responses and an in-memory repository, not live
career sites or production databases.
