# Phase 5: source coverage, normalization and mapping

## Shared pipeline

Manual administration and the disabled-by-default scheduler still call the same
`IJobSourceRunner`. Its per-record flow is now:

`provider -> IExternalJobNormalizer -> category resolver -> existing ingestion -> URL/fingerprint/fuzzy dedup`

`ExternalJobNormalizer` is a deterministic, side-effect-free application service.
It returns an immutable DTO copy, makes no database/network calls, and does not
change the canonicalizer, fingerprint algorithm, fuzzy threshold or company scope.
Direct ingestion callers retain the existing ingestion contract; aggregation runs
always normalize before ingestion. No existing jobs are batch-renormalized.

## Normalized fields

- Title/company: trim and collapse Unicode whitespace; preserve case and symbols,
  including `.NET`, `C#`, `C++` and `Node.js`. No company renaming or title inference.
- Location: trim/collapse whitespace, remove empty comma-separated components,
  standardize comma spacing. Preserve slashes, dashes, city/state/country content
  and `Remote - US`. No geocoding, country guesses or paid services.
- Description/requirements/responsibilities/benefits: blank becomes null; trim each
  line and collapse in-line whitespace; preserve line breaks and at most one blank
  line between paragraphs. Ingestion retains its existing empty description default.
- Application URL/external ID: trim only. URL canonicalization remains separate.
- Structured external category/type labels: trim/collapse whitespace.
- Valid explicitly supplied enum values take precedence over raw structured labels.
  Invalid enum values are discarded. Unrecognized labels remain null; new Jobs
  retain the existing unspecified/default enum behavior when null.

Greenhouse descriptions are explicitly marked `DescriptionIsHtml`. A small text
extractor removes tags/comments, discards script/style bodies (including unclosed
bodies), observes quoted tag attributes, and preserves block/list boundaries.
Framework HTML entity decoding is used; encoded whole Greenhouse block fragments
are decoded before extraction, while entities within literal HTML are decoded as
text afterward to preserve examples like `List<T>`. Normalized copies clear the
HTML flag, so subsequent normalization is idempotent. Lever/Ashby use documented
plain-text descriptions; they are not treated as HTML.

This is text extraction, **not an HTML sanitizer or browser parser**. Never render
its output as trusted HTML; clients must escape plain text. This backend has no
frontend rendering implementation or sanitized-HTML description contract to
change. No curated description or other existing canonical content is replaced.
Complex/nested encoded or severely malformed markup is not a full HTML5 parsing
contract; unsupported markup can lose formatting. No new parsing dependency.

## Type mappings

Structured labels are matched case-insensitively after whitespace/hyphen removal.

| Employment label | Existing enum |
| --- | --- |
| full time / full-time / fulltime | FullTime |
| part time / part-time / parttime | PartTime |
| contract / contractor | Contract |
| intern / internship | Internship |
| freelance | Freelance |
| temporary | Temporary |

| Workplace label | Existing enum |
| --- | --- |
| remote | Remote |
| hybrid | Hybrid |
| on-site / onsite / on site / office | OnSite |

Unknown/combined labels, including `remote/hybrid`, `remote - us`, `unspecified`
and `permanent`, are not mapped. In particular, location, title and description
never determine workplace/employment classification. No experience/salary guesses.

## Explicit category mapping

Priority, checked separately for every normalized record:

1. Explicit `RawExternalJob.CategoryId`, if an existing nondeleted category.
2. `JobAggregation:CategoryMappings:<external-label>`, if a valid existing category.
3. Existing `JobAggregation:SourceCategories:<source-guid>` fallback.
4. Null: duplicates can still refresh; genuinely new jobs are skipped by ingestion.

External keys use case-insensitive, trimmed/collapsed-whitespace comparison. Invalid
GUIDs, empty IDs, missing/deleted categories and conflicting normalized keys fail
closed to the next fallback. No category creation, title guessing or AI assignment.
Configuration reload works through the existing options monitor. Optional malformed
category values do not impose new API startup failures. Scheduler validation remains
unchanged.

Environment examples (illustrative fake IDs only; no appsettings changes):

```text
JobAggregation__CategoryMappings__engineering=11111111-1111-4111-8111-111111111111
JobAggregation__SourceCategories__22222222-2222-4222-8222-222222222222=33333333-3333-4333-8333-333333333333
```

Use simple external labels for portable environment variable names. Mappings are
global exact label mappings, not provider-specific taxonomy inference. Lever/Ashby
choose a nonblank department, otherwise team; they do not try a different team when
an existing department has no mapping. Greenhouse uses only a single department;
multiple departments are ambiguous and fall back to source configuration. Existing
admin source category display continues to show the source fallback, not a single
invented category for all provider jobs.

## Provider coverage and capabilities

| Provider | Location | Employment | Workplace | Category | Description | Stable ID |
| --- | --- | --- | --- | --- | --- | --- |
| Greenhouse | location.name | absent | absent | one department | content HTML | numeric id |
| Lever | categories.location | categories.commitment | workplaceType | department, else team | descriptionPlain | id |
| Ashby | location | employmentType | workplaceType | department, else team | descriptionPlain | not in public contract |

These are optional capabilities, not promises that each record contains data.
A new runtime capability abstraction would have no consumer: the runner processes
nullable DTO metadata uniformly. This table documents capabilities without adding
unused interfaces or database fields.

- Greenhouse endpoint stays `GET https://boards-api.greenhouse.io/v1/boards/{board-token}/jobs?content=true`.
- Lever endpoint stays `GET https://api.lever.co/v0/postings/{site-token}?mode=json`.
- Ashby uses `GET https://api.ashbyhq.com/posting-api/job-board/{job-board-name}`.
  `AtsIdentifier` is the job-board name, not a URL or API key. Only `isListed: true`
  records are included; false, missing or malformed visibility is excluded. This
  avoids exposing private-link postings. The public contract does not expose a
  stable ID, so none is fabricated; existing URL/fingerprint matching is used.
  `jobUrl` supplies the canonical hosted posting link. Secondary locations,
  compensation, `isRemote` fallback and application submission are not implemented.

Ashby was selected as the one additional provider because its official public API
documents unauthenticated listings and structured types. No private endpoints,
scraping, browser automation, CAPTCHA workarounds or login. Other ATS providers
were not added. Requests use a fixed HTTPS host, encoded identifier, ten-second
timeout, one fetch per source attempt, and existing scan interval/batch/concurrency
limits. HTTP 429/network failures fail the source and respect existing attempt
bookkeeping; there is no new immediate retry loop or external call in unit tests.

Official contracts inspected:

- [Greenhouse Job Board API](https://docs.greenhouse.io/job-board.html)
- [Lever Postings API](https://github.com/lever/postings-api)
- [Ashby public Job Postings API](https://developers.ashbyhq.com/docs/public-job-posting-api)

`AtsType.Ashby = 3` is appended; Custom=0, Greenhouse=1, Lever=2 are unchanged.
EF stores this as an integer, not a PostgreSQL enum/check constraint. Adding the
value does not change the model or require migration. No source is auto-created.
Admin validation now requires an Ashby identifier just as for the other providers.

## Malformed records and safety

Provider JSON is read field by field. Wrong-type optional fields become null, so
one malformed field does not discard other usable fields. Missing/wrong-type titles
become empty and are skipped by existing ingestion. Invalid URLs/missing companies
still follow existing Invalid/CompanyNotFound outcomes. Malformed envelopes/JSON,
HTTP errors and cancellation are not reported as successful empty source runs.
Record-level exceptions remain isolated by the existing runner. Source counters
remain counts of returned DTOs (Ashby excludes nonpublic records before this point).

Matched jobs only refresh LastSeen and fill missing aggregation metadata. Title,
description, salary, company, category, publication state, type values, referrals,
recruiter contacts and application state are not overwritten by normalization.
New jobs are Draft. Existing Adzuna discovery and AI Apply adapters are unchanged.

Scheduler polling, disabled default, due selection, bounded batch/concurrency,
cancellation, failure isolation and process-local run guard remain unchanged.
Manual-run authorization/auditing and the shared runner remain unchanged.
Distributed coordination remains an existing limitation.

## Validation

Focused normalization/type/category/provider/pipeline tests use fake HTTP handlers
and EF InMemory. Regression includes all Phase 1–4 suites and referral/lifecycle.
The full non-database suite excludes the three live PostgreSQL tests. Builds retain
TreatWarningsAsErrors; only the existing allowed test analyzer suppression is used.
Offline EF pending-model validation uses a dummy localhost design-time connection
and does not connect to a database. Zero migrations/schema changes and no live DB
operations. Stash, SQL inspection artifact and unrelated files remain untouched.
