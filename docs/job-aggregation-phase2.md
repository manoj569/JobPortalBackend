# Phase 2 external job ingestion

`IJobSourceRunner` fetches public ATS postings and passes each record to
`IJobIngestionService`. Greenhouse uses `AtsIdentifier` as its board token
(`v1/boards/{token}/jobs?content=true`); Lever uses it as its site token
(`v0/postings/{token}?mode=json`). `CareerPageUrl` remains human-facing.
There is no scheduler or API endpoint in this phase.

## Category decision

The existing category seed defines ten specific categories, not a generic
Other/General/Uncategorized category. Category management supports explicit
administrator-created categories but defines no generic fallback contract.
Therefore ingestion does not infer categories from job titles, select an
arbitrary category, or create categories.

New jobs require an explicit `RawExternalJob.CategoryId` referencing an existing,
non-deleted category. Missing, empty or unknown IDs produce `Invalid` and the
runner counts them as skipped. Greenhouse and Lever do not currently provide
this mapping, so their genuinely new jobs remain skipped until an explicit
category mapping/configuration is implemented. They can still refresh existing
canonical jobs without a category ID. No environment-specific fallback is set.

## Preservation and operational limits

Companies must already exist. Deduplication retains Phase 1 URL, fingerprint,
then bounded company-scoped fuzzy matching. Existing jobs are reloaded for
tracked updates to LastSeenAtUtc and missing FirstSeenAtUtc/FingerprintHash;
curated fields, company, referral/recruiter relations and publication state
remain unchanged. New jobs are Draft and require moderation.

Individual ingestion failures do not stop remaining records. Caller cancellation
propagates. Source failures retain the previous successful-run timestamp and
store a fixed error message rather than potentially sensitive exception text.

This phase has zero schema changes and leaves Adzuna discovery and AI Apply ATS
adapters unchanged. Concurrent creation can still produce duplicates: Phase 1's
fingerprint index is non-unique, and Phase 2 adds no uniqueness constraint.
