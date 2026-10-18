# Real PostgreSQL Talentd coverage

Executed against a new task-owned PostgreSQL 17.11 container, loopback only, not production. These are combined integration tests with multiple assertions, not 22 separately named test methods.

| Required case | Executed integration evidence |
|---|---|
| 1–4 Source registration, talentd key, enabled, auto publish | TalentdRegistrationAndPayloadIdempotencyAreDurable; SourceMigrationAppliesRollsBackReappliesAndRepeatedSeedCreatesOneSource |
| 5 Content-neutral import | ApprovedWorkerContractPersistsPublishesUpsertsAndReconcilesOnPostgres: senior, 5–8 years, GB, hybrid, unverified employer |
| 6 New job persistence | Same test, Created=1 and actual job row |
| 7 Automatic publication | Same test, real JobAutoPublishService → JobService, Job.Status=Published |
| 8 PublishedCount | Same test + TenDeterministicJobsHaveDurablePublicationAndPhaseTimings; durable run count 1/10 |
| 9 Item PublicationStatus | Actual receipt Published, NULL PublicationError |
| 10 Direct ApplyUrl | Job and receipt retain Workable destination |
| 11 DiscoveredUrl | Job and receipt retain https://www.talentd.in/jobs/example |
| 12 CanonicalJobUrl | Actual employer canonical URL remains separate from discovery URL |
| 13 Stable identity/upsert | Fresh ImportId, same acquisition identity updates the same JobId |
| 14 Second import no duplicate | Exact request durable replay and later update both checked |
| 15 Distinct identities | Different identities survive same title/company/location; ten-job batch also shares URL and creates ten rows |
| 16 Company resolution | CompanyName/website resolves actual Company FK without employer verification |
| 17 Skills | Actual JobSkill joins, C#/c# duplicate suppression, SQL retained |
| 18 Rich fields | Salary/currency/period, education, responsibilities/requirements/benefits, posting/deadline/observation dates, logo and full source JSON round-trip |
| 19 IN_PROGRESS | StartPayloadRunAsync produces durable Running row; status returns IN_PROGRESS |
| 20 COMPLETED | Completed status returns run counters and indexed receipt |
| 21 NOT_FOUND | Absent fresh ImportId returns null in store (controller 404 checked by status suite) |
| 22 Fingerprint/idempotency | Exact payload replays same RunId; changed description under same ImportId throws conflict; separate registration test checks actual durable fingerprint comparison |

Additional executed integration: TalentdLogoOnlyFillsMissingAndRejectsUnsafeUrls preserves trusted logo, ignores loopback URL, fills missing safe URL. Migration test uses a separate local database, rolls back only the Talentd migration, preserves source identity, re-applies and executes the seed SQL again without a duplicate.

The approved worker fixture was generated offline. Random per-test namespaces isolate synthetic test identities and ImportIds; the fixture job fields/URLs are deterministic. No historical acquisition ledger or ImportId is loaded or replayed.
