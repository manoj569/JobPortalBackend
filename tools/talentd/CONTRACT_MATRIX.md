# Approved worker → backend contract matrix

The checked-in `Fixtures/Talentd/approved-worker-request.json` is generated offline by calling the approved worker's actual `wire()` and `request_for()` functions. No crawler, backend client, ledger or production connection is constructed. The PostgreSQL round-trip test deserializes its camelCase request and compares the entire `sourceMetadata` object with persisted `Job.SourceMetadataJson.discovery.extracted` using JSON structural equality.

| Python normalized/wire field | .NET DTO field | Domain destination | Persisted | Detail |
|---|---|---|---|---|
| title / title | Title | Job.Title | Yes | 250-character technical limit |
| company / companyName | CompanyName | Company.Name + Job.CompanyId | Yes | Existing company resolution; no approval/verification gate |
| company_website / companyWebsiteUrl | CompanyWebsiteUrl | Company.WebsiteUrl/domain and metadata | Yes | Existing company data preserved by resolution rules |
| logo / sourceMetadata.talentd.logo | SourceMetadata; CompanyLogoUrl also supported | Company.LogoUrl and metadata | Yes | Fill only missing safe logo; preserve trusted existing logo |
| location / locationText | LocationText | Job.Location + metadata | Yes | Existing composed location display |
| addresses / sourceMetadata.talentd.addresses | SourceMetadata | Metadata | Yes | No separate Job city/state columns |
| explicit addressCountry / country | Country | Location/countryCodes evidence + metadata | Yes | Talentd wire explicitly overrides generic worker's India default |
| experience / experienceText | ExperienceText | talentd.experience evidence | Yes | No fabricated numeric range |
| minimum_experience / minimumExperienceYears | MinimumExperienceYears | Job.MinimumExperienceYears | Yes | Structured range and existing classifier/admin overrides |
| maximum_experience / maximumExperienceYears | MaximumExperienceYears | Job.MaximumExperienceYears | Yes | Structured range |
| fresher / sourceMetadata.talentd.fresher | SourceMetadata | Metadata | Yes | No standalone fresher boolean column |
| employment_type / employmentType | EmploymentType | Job.EmploymentType + metadata | Yes | Known singleton value maps enum; multi-value evidence retained |
| work_arrangement / workplaceType | WorkplaceType | Job.WorkplaceType + metadata | Yes | Remote/hybrid/onsite, no geographic eligibility gate |
| remote_eligibility (unset by Talentd) | RemoteEligibility | Evidence/metadata when supplied | N/A when absent | No invented India eligibility |
| salary.value.minValue/maxValue | SourceMetadata | Job.MinimumSalary/MaximumSalary | Yes | Explicit amounts, no conversion |
| salary.currency | SourceMetadata | Job.CurrencyCode | Yes | Explicit three-letter source currency |
| salary.value.unitText | SourceMetadata | Job.SalaryPeriod | Yes | YEAR/MONTH/HOUR; unsupported units stay metadata |
| salary_text | SourceMetadata | Metadata | Yes | Original text retained exactly |
| batch_years | SourceMetadata | Metadata array | Yes | No new relational schema |
| skills / skills | Skills | Skill + JobSkill and metadata | Yes | Obvious whitespace/case duplicates normalized; refresh adds skills |
| degree | SourceMetadata | EducationRequirement fallback + metadata | Yes | Existing 200-character structured field; full evidence retained |
| education | SourceMetadata | EducationRequirement + metadata | Yes | Scalar/array text if it fits; objects/long text remain metadata |
| eligibility in description_sections/structured | SourceMetadata | Metadata + existing description text | Yes | No invented top-level worker field or policy gate |
| category/categories | SourceMetadata (Category DTO also supported) | Metadata; existing taxonomy if explicitly resolved | Yes | No guessed taxonomy mapping for raw source categories |
| posted_at / postedAt | PostedAt | Job.SourcePostedAtUtc + metadata | Yes | Source timestamp |
| updated_at | SourceMetadata | Metadata | Yes | Not substituted for platform UpdatedAtUtc |
| deadline / expiresAt | ExpiresAt | Job.ExpiresAtUtc, receipt.SourceExpiresAtUtc | Yes | Source deadline preserved |
| description / description | Description | Job.Description + original metadata | Yes | Existing safe HTML-to-text policy; request-byte bounds |
| responsibilities | SourceMetadata | Job.Responsibilities + metadata | Yes | Existing plain-text normalizer |
| qualifications | SourceMetadata | Job.Requirements + metadata | Yes | No new qualifications column |
| benefits | SourceMetadata | Job.Benefits + metadata | Yes | Existing field |
| discovered_url / discoveredUrl | DiscoveredUrl | Job.DiscoveredUrl + receipt | Yes | `https://www.talentd.in/jobs/example` fixture |
| apply_url / canonicalJobUrl | CanonicalJobUrl | Job.CanonicalJobUrl + receipt.CanonicalUrl | Yes | Approved worker intentionally uses direct employer URL |
| apply_url / applyUrl | ApplyUrl | Job.ApplicationUrl + receipt.ApplyUrl | Yes | Workable URL and unfamiliar employer-domain tests |
| observation timestamp / sourceLastSeenAt | SourceLastSeenAt | Job/receipt.LastVerifiedAtUtc | Yes | Observation, not employer vacancy verification |
| identity / externalJobId | ExternalJobId | Job.AcquisitionExternalId + receipt.ExternalId | Yes | Approved worker hashing; normalized stable source identity |

No useful field accepted by the contract was found to be accidentally dropped in the round-trip checks. Fields without dedicated columns remain structurally present in the JSON evidence. No new product features, worker edits, candidate DTO fields or database columns were introduced by this continuation.
