# Talentd field audit

Inspected: approved worker `models.py`, `talentd_parse.py:wire`, `talentd_import.py`, backend acquisition DTO/service/store, RawExternalJob, Job/Company/JobSkill, entity configurations, ingestion normalizer/classification, JobAutoPublishService and JobService.

A = already supported end-to-end; B = domain supported but worker DTO path missing; C = mapper dropped supported evidence; D = no dedicated domain field. Metadata below means the original `sourceMetadata.talentd` record retained inside Job.SourceMetadataJson, not a new column. No new DTO fields are needed: reading the worker's existing metadata keeps request fingerprints unchanged.

| Worker field / concept | Audit | Final persistence |
|---|---|---|
| Title | A | Job.Title, 250 characters |
| Company | A | Company via normalized name/domain resolution, no verification or approval requirement |
| Company logo (`talentd.logo`) | B/C | Fill missing Company.LogoUrl only; safe URL <=2048; atomic conditional update; original metadata retained |
| Location | A | Job.Location (composed display, <=250); original metadata |
| City / state | B | DTO City/State supported when supplied; approved worker addresses stay in metadata; its locationText is persisted |
| Country | A | Explicit country code metadata; composed display; full addresses in metadata; no India default for Talentd |
| Work arrangement | A | WorkplaceType enum when recognized; original evidence in metadata |
| Employment type | A | Existing enum for one recognized value; multi-valued/unsupported types retained in metadata |
| Experience text | A | Existing envelope metadata; raw Talentd evidence retained |
| Min/Max experience | A | Existing structured Job fields and classifier override policy, no forced zero default |
| Fresher indicator | D | Metadata; no separate boolean column |
| Salary text | D | Original talentd.salary_text metadata |
| Salary min/max | B/C | Existing decimal(18,2) Job columns from explicit JSON-LD baseSalary.value minValue/maxValue or scalar value |
| Salary currency | B/C | Explicit three-letter currency only, uppercase; never guessed |
| Salary period | B/C | Explicit YEAR/MONTH/HOUR to Yearly/Monthly/Hourly; unsupported period retained in metadata |
| Batch years | D | Exact array in metadata; no batch-year relation exists |
| Skills | A (refresh gap C) | Skill/JobSkill, trim/collapse spaces and casing duplicates; Talentd refresh adds newly supplied skills; missing skills do not erase reviewed associations |
| Degree | B/C | EducationRequirement fallback if scalar/strings <=200, full metadata always retained |
| Education | B/C | EducationRequirement for scalar/string-array <=200; objects/long text stay in metadata |
| Eligibility | D | Evidence/description metadata; no inferred acceptance barrier |
| Category | A/B | Existing DTO category resolution retained; worker category/categories retained in metadata (no guessed taxonomy mapping) |
| PostedAt | A | Job.SourcePostedAtUtc; source date also in metadata |
| UpdatedAt | D | Source `updated_at` metadata; not substituted for platform modification time |
| ClosingDate | A | Worker expiresAt to Job.ExpiresAtUtc, receipt SourceExpiresAtUtc |
| Description | A | Existing HTML-to-text normalizer, suppresses script/style; Talentd has no artificial 16000-character gate, bounded by request bytes; Job.Description is text |
| Responsibilities | B/C | Existing Job.Responsibilities, normalized plain text |
| Qualifications | B/C | Existing Job.Requirements, normalized plain text |
| Requirements | D | Approved worker has qualifications/description_sections/structured evidence; preserved in metadata/description, no guessed section names |
| Benefits | B/C | Existing Job.Benefits, normalized plain text |
| DiscoveredUrl | A | Talentd detail URL in Job + import receipt |
| CanonicalJobUrl | A | Worker direct employer/ATS URL in Job + receipt |
| ApplyUrl | A | Job.ApplicationUrl + receipt.ApplyUrl; first choice, unknown legitimate public DNS domains accepted |
| LastVerifiedAtUtc | A | Worker sourceLastSeenAt mapped to Job + receipt; observation timestamp, not a claim of employer vacancy verification |

The metadata can retain source HTML as JSON evidence; rendered Job text fields use the existing plain-text normalizer. No evidence is treated as executable markup. The API retains its configured request-size limit (default 3,000,000 bytes), maximum batch size, title/company/location/skill limits, experience range checks and URL safety checks. Education beyond the existing 200-character column remains untruncated in metadata. No salary annualization or text parsing was added. Existing description classifier behavior is preserved; structured provider salary takes precedence under existing classification rules, including administrator overrides.
