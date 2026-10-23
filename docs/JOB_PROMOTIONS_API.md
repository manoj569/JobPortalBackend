# Promoted Jobs API

Promotions are an additive display configuration for an existing `Job`. They do not create jobs, alter job category or publication state, change `IsFeatured`, or add an application workflow. An enabled promotion is returned only while its schedule is active and the linked job passes the same availability query used by public job search and job details.

The public job availability rule requires a published, non-hidden, non-deleted job; a non-deleted company and category (when present); and a publication timestamp. It also applies the existing expiry exception for the `website-crawler` and `talentd` acquisition sources. This logic is reused directly through `PublicJobRepository.AvailableJobs`.

## Administrator endpoints

All administrator routes require the existing `Administrator` role.

`PUT /api/admin/jobs/{jobId}/promotion` creates or replaces the promotion configuration. It is a full configuration update; banner bytes are managed separately. Enabling a promotion for an unavailable job returns a validation error.

```json
{
  "isEnabled": true,
  "startsAtUtc": null,
  "endsAtUtc": "2026-12-31T23:59:59Z",
  "displayPriority": 0
}
```

`GET /api/admin/jobs/{jobId}/promotion` returns the settings, persisted job slug, and current eligibility. `DELETE /api/admin/jobs/{jobId}/promotion` disables the promotion and preserves the configuration and original job. `GET /api/admin/job-promotions?pageNumber=1&pageSize=20&isEnabled=true` returns a bounded, paginated configuration list.

`PUT /api/admin/jobs/{jobId}/promotion/banner` accepts one multipart `file` upload. JPEG, PNG, and WebP bytes are checked against the declared media type and filename extension; the upload must be 1 byte to 5 MiB. The file is stored as PostgreSQL `bytea`, and its URL is the API route `/api/job-promotions/{jobId}/banner`. No arbitrary URL can be configured as a banner. This uses the existing durable PostgreSQL deployment and requires no object-storage credentials. Company logos remain available separately as `companyLogoUrl` fallback content.

## Public endpoints

`GET /api/job-promotions/active` returns at most 24 promoted jobs in priority, newest-configuration, then Job ID order. Its JSON is wrapped in the existing `ApiResponse` envelope. Example `data[0]` entry:

```json
{
  "jobId": "d6f33721-c8e3-4df6-9f1f-97f6ae31a7f3",
  "jobSlug": "graduate-engineer-trainee",
  "title": "Graduate Engineer Trainee",
  "companyName": "Wipro Limited",
  "companyLogoUrl": "https://careerharbor.in/company-logo.webp",
  "bannerImageUrl": "/api/job-promotions/d6f33721-c8e3-4df6-9f1f-97f6ae31a7f3/banner",
  "jobCategory": "Fresher",
  "location": "Gurugram, Haryana",
  "workplaceType": "OnSite",
  "employmentType": "FullTime",
  "minimumExperienceYears": 0,
  "maximumExperienceYears": null,
  "educationRequirement": "B.E./B.Tech/MCA",
  "minimumSalary": null,
  "maximumSalary": null,
  "currencyCode": null,
  "skills": ["Manual Testing", "SQL"],
  "postedAtUtc": "2026-10-23T00:00:00Z",
  "detailsUrl": "/dashboard/jobs/graduate-engineer-trainee"
}
```

Values above are illustrative; nullable fields and skills are read from the linked persisted Job. The banner URL is an application-owned route relative to the API origin. Public access to that route repeats schedule and public-job eligibility checks, so disabling a promotion or closing/deleting/hiding/unpublishing/expiring its Job stops both the card and banner response.

The existing Hiring Drives source remains `GET /api/public/hiring-drives/home`. Clients can add promoted job cards as a second source in the same Hiring Opportunities section, keep the existing drive cards, and deduplicate promoted job cards against internal Hiring Drives by `jobId`. The promoted card's `jobId` and `jobSlug` identify the same original Job. Navigate using `jobSlug` and the existing `/dashboard/jobs/{jobSlug}` details route; do not search by ID to resolve a slug. External Hiring Drive click destinations and application handling are unaffected.

## Database and rollout

Migration: `20261023182132_AddJobPromotions`. It adds a `JobPromotions` table with a unique JobId foreign key, UTC schedule fields, administrator audit references, a priority index, and database checks for dates, priority, banner type, and 5 MiB content limit. It does not alter existing Jobs or Hiring Drives rows.

Apply this migration through the normal reviewed deployment migration process before enabling the new API routes. No campaign or promotion rows are seeded. PostgreSQL stores banner bytes durably with the database; operators should monitor database size and move to configured durable object storage if promotional media volume grows materially.

## Manual verification

1. Apply the migration to a disposable/staging database.
2. As an administrator, enable a currently public Job and confirm the response includes its persisted `jobSlug` and eligibility.
3. Call `/api/job-promotions/active`; confirm card fields match the original Job and `detailsUrl` points to the existing slug route.
4. Upload a valid image, fetch its banner route, then try an invalid extension/content pair and an upload larger than 5 MiB.
5. Disable the promotion and separately hide, close, unpublish, expire, or soft-delete its Job. Confirm the card and banner are no longer public.
6. Confirm the existing `/api/public/hiring-drives/home` and click routes still return the same records and destinations.
