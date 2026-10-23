# Hiring Drives API Contract

This contract reflects the current CareerHarbor backend implementation. Routes are relative to the API origin. JSON property names use ASP.NET Core's default `System.Text.Json` web naming policy (camelCase); enum fields in this feature are strings, not numeric enums. Responses marked `ApiResponse<T>` use `{ "data": ... }`.

## Public list

`GET /api/public/hiring-drives`

Anonymous. No request body or query parameters. Successful response is HTTP 200 with an `ApiResponse<IReadOnlyCollection<PublicHiringDriveResponse>>` envelope. `data` is an array. The query includes only nondeleted, active drives where `StartAtUtc <= server UTC now`, `EndAtUtc` is null or `EndAtUtc >= now`, and an internal JobId still refers to a nondeleted job. Sort is `displayOrder` ascending, then `createdAtUtc` descending, with ID as a deterministic final tie-breaker.

Each public item has exactly these JSON keys (nullable values serialize as JSON `null`; destination's unused nullable member is omitted):

| JSON property | Type | Notes |
|---|---|---|
| `id` | string UUID | Drive ID |
| `title` | string | |
| `subtitle` | string or null | |
| `badgeText` | string or null | |
| `badgeType` | string or null | Current allowed strings are `hiring`, `walkIn`, `openings`, `featured` |
| `company` | object or null | `{ id, name, logoUrl }`; logoUrl is string or null |
| `imageUrl` | string or null | |
| `locationText` | string or null | |
| `experienceText` | string or null | |
| `isSponsored` | boolean | |
| `destination` | object | Internal `{ type, jobId }` or external `{ type, url }` |

Not returned: description, companyId as a separate property, ApplyUrl as a separate property, start/end dates, display order, analytics counts, active flag, or audit fields. A company ID is nested under `company.id`. Internal jobs take precedence when both JobId and ApplyUrl are set.

Example matching the controller, DTOs and serializer:

```json
{
  "data": [
    {
      "id": "5c0911bb-b080-4f40-b461-76993d889ba1",
      "title": "Software hiring drive",
      "subtitle": "Graduate Engineer",
      "badgeText": "Hiring Now",
      "badgeType": "hiring",
      "company": {
        "id": "fa29151f-7372-4e59-a19a-6dc18f66e516",
        "name": "Example Company",
        "logoUrl": "https://cdn.example.test/logo.png"
      },
      "imageUrl": null,
      "locationText": "Bengaluru",
      "experienceText": "0-1 year",
      "isSponsored": false,
      "destination": {
        "type": "internalJob",
        "jobId": "e9c22e8a-0abd-49dc-95ca-2f31ac4cb523"
      }
    }
  ]
}
```

The values above illustrate shape only; no such campaign is seeded. Empty result is `{"data":[]}`.

## Click tracking

`POST /api/public/hiring-drives/{id}/click`

Anonymous; `{id}` must be a UUID. There is no request body and the client cannot provide a destination. Success is HTTP 200 with `ApiResponse<HiringDriveClickResponse>`. `data` contains `destinationType` and exactly one applicable optional field. The click is counted for the server-selected destination under a database row lock. Unavailable, inactive, future, expired, soft-deleted, or now-invalid internal-job drives return 404. The endpoint is also subject to the global API limiter (120 requests/minute per user/IP partition) and a HiringDriveAnalytics limiter (30 requests/minute per remote IP/application instance); the first reached limit returns 429.

A. Internal Job:

```json
{
  "data": {
    "destinationType": "internalJob",
    "jobId": "e9c22e8a-0abd-49dc-95ca-2f31ac4cb523",
    "jobSlug": "software-engineer-example-1234abcd"
  }
}
```

B. External URL:

```json
{
  "data": {
    "destinationType": "external",
    "url": "https://careers.example.test/apply"
  }
}
```

The response is flat inside `data`; it is not a nested `destination` object. For an internal job, `jobId` and `jobSlug` are returned from the linked persisted Job. For an external destination, `url` is returned. Fields that do not apply are omitted due to property-level JSON ignore attributes. The slug comes from the same published/visible/unexpired Job resolved on the server; clients should navigate directly to the existing job-detail route using `jobSlug`. Do not issue a job search to resolve `jobId`.

## Impression tracking

`POST /api/public/hiring-drives/impressions`

Anonymous. Request JSON:

```json
{ "driveIds": ["5c0911bb-b080-4f40-b461-76993d889ba1"] }
```

IDs must contain 1–50 nonempty UUIDs. Duplicate IDs in one batch are deduplicated. The request body limit is 4096 bytes. Only currently eligible active drives are incremented; invalid/not-found IDs are ignored. Success is HTTP 204 No Content, with no response JSON or envelope. Invalid model JSON/body binding is normally HTTP 400; FluentValidation failures are HTTP 400 `ApiError` as described below. The endpoint has the same global and feature-specific rate limits as click tracking. GET list does not increment impressions; the frontend must call this endpoint when cards actually become visible.

## Admin list

`GET /api/admin/hiring-drives?pageNumber=1&pageSize=20`

Requires an authenticated user with role `Administrator`. Query fields are optional, defaults `pageNumber=1`, `pageSize=20`; page number must be positive and page size 1–100. There is no filtering. Sorting is display order ascending then creation time descending then ID ascending. HTTP 200, envelope `data` is a `PagedResponse`:

```json
{
  "data": {
    "items": [
      {
        "id": "5c0911bb-b080-4f40-b461-76993d889ba1",
        "campaign": {
          "title": "Software hiring drive",
          "startAtUtc": "2026-10-22T00:00:00Z",
          "companyId": "fa29151f-7372-4e59-a19a-6dc18f66e516",
          "subtitle": null,
          "description": null,
          "badgeText": "Hiring Now",
          "badgeType": "hiring",
          "imageUrl": null,
          "locationText": "Bengaluru",
          "experienceText": "0-1 year",
          "jobId": null,
          "applyUrl": "https://careers.example.test/apply",
          "endAtUtc": null,
          "displayOrder": 0,
          "isActive": false,
          "isSponsored": false
        },
        "impressionCount": 0,
        "clickCount": 0,
        "ctr": 0,
        "createdAtUtc": "2026-10-23T00:00:00Z",
        "updatedAtUtc": null,
        "createdByUserId": "ebfc0b6f-b338-49da-899e-6c89495de648",
        "updatedByUserId": null
      }
    ],
    "pageNumber": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

The example is illustrative. Admin listing currently includes soft-deleted drives? The EF base entity query filter hides deleted drives, so it does not. No state or company filter is implemented.

## Admin detail

`GET /api/admin/hiring-drives/{id}` requires Administrator. Success HTTP 200, `ApiResponse<AdminHiringDriveResponse>` (`data` contains the same single object shape shown in the list). Missing or soft-deleted ID is HTTP 404.

## Admin create

`POST /api/admin/hiring-drives` requires Administrator. Send the request object defined under DTO definitions below (not wrapped in `data`). Success is HTTP 201 Created with `ApiResponse<AdminHiringDriveResponse>` and a Location header for the detail route.

## Admin update

`PUT /api/admin/hiring-drives/{id}` requires Administrator. Send the same complete request DTO; this is a replacement of editable campaign fields, not a partial patch. Success HTTP 200 with `ApiResponse<AdminHiringDriveResponse>`. Analytics counts and creation metadata are preserved. Missing/soft-deleted drive is HTTP 404.

There is no separate activate/deactivate endpoint. Set `isActive` in the full PUT body. New campaigns default to inactive if omitted on create.

## Admin delete/deactivate

`DELETE /api/admin/hiring-drives/{id}` requires Administrator. Soft-deletes and deactivates the campaign, retains analytics/audit record, appends an audit event. Success HTTP 204 with no body. Repeated delete or missing ID returns 404 because the standard base-entity filter hides soft-deleted records.

## DTO definitions

Create and update use `SaveHiringDriveRequest`; JSON property names are camelCase:

| Property | Type | Required / nullable | Validation and behavior |
|---|---|---|---|
| `title` | string | Required, nonnull | Not blank; max 200 characters; trimmed before storage |
| `startAtUtc` | ISO date-time string | Required | Must be present and have UTC kind (`Z` recommended) |
| `companyId` | UUID or null | Optional, nullable | If provided, must reference a nondeleted existing Company |
| `subtitle` | string or null | Optional, nullable | Max 300 |
| `description` | string or null | Optional, nullable | Max 10000; not returned publicly |
| `badgeText` | string or null | Optional, nullable | Max 60 |
| `badgeType` | string or null | Optional, nullable | `hiring`, `walkIn`, `openings`, `featured`, or null |
| `imageUrl` | string or null | Optional, nullable | Max 2048; if provided must be an absolute HTTPS URL with host, no credentials/control characters/backslash |
| `locationText` | string or null | Optional, nullable | Max 200 |
| `experienceText` | string or null | Optional, nullable | Max 100 |
| `jobId` | UUID or null | Optional, nullable | If provided, must reference a nondeleted existing Job; takes destination precedence |
| `applyUrl` | string or null | Optional, nullable | Max 2048; if provided must be an absolute HTTPS URL with host, no credentials/control characters/backslash |
| `endAtUtc` | ISO date-time string or null | Optional, nullable | If provided must have UTC kind and be later than `startAtUtc` |
| `displayOrder` | integer | Optional; defaults 0 | Must be >= 0 |
| `isActive` | boolean | Optional; defaults false | Public list requires true |
| `isSponsored` | boolean | Optional; defaults false | |

At least one of `jobId` or `applyUrl` must be present. Both may be present. Client should supply all editable properties on PUT. Admin response wraps these request properties under `campaign` and adds `id`, analytics, and audit fields.

`AdminHiringDriveResponse`: `id` UUID, `campaign` SaveHiringDriveRequest, `impressionCount` integer (64-bit), `clickCount` integer (64-bit), `ctr` decimal number, `createdAtUtc` date-time, `updatedAtUtc` nullable date-time, `createdByUserId` nullable UUID, `updatedByUserId` nullable UUID. CTR is `clickCount / impressionCount * 100`, or zero when impressions is zero. It is not rounded in service code; JSON numeric formatting is serializer-controlled.

`PublicHiringDriveResponse`: fields listed in Public list. Nested company has `id` UUID, `name` string, `logoUrl` string or null. Public destination `type` is the exact string `internalJob` or `external`; internal has UUID `jobId`, external has string `url`.

## Enum values

Hiring Drives does not use CLR enum properties. `badgeType` is an untyped string restricted by validation to `hiring`, `walkIn`, `openings`, `featured` (or null). Destination values are server-generated exact strings `internalJob` and `external`. `destination.type` is used on list items; click response calls the equivalent property `destinationType`.

## Validation rules

- FluentValidation failures from admin save/list and impression validation are HTTP 400 with `ApiError` (`code: validation_error`, `message`, `errors` keyed by property name).
- Invalid CompanyId/JobId is also a FluentValidation 400.
- JSON/model binding errors handled by `[ApiController]` use HTTP 400 `ValidationProblemDetails` (`type`, `title`, `status`, `errors`); some malformed-body errors may instead reach the global `ApiError` handler.
- HTTP and other unsafe URL schemes are rejected. HTTPS syntax alone does not establish that an employer or hiring claim is authentic; administrative verification is operational.
- DB schema additionally checks date ordering, nonnegative counters/order, and that a destination exists.

## Error responses

This feature's service throws `ValidationException` (400), `NotFoundException` (404), while ASP.NET authentication/authorization and rate limiting handle their respective statuses. No feature path explicitly produces 409 or 422. The general global handler uses this shape for handled errors:

```json
{ "code": "validation_error", "message": "One or more validation errors occurred.", "errors": { "Title": ["'Title' must not be empty."] } }
```

NotFound example: `{"code":"not_found","message":"Hiring drive was not found."}`. Click unavailable message is `Hiring drive is unavailable.`. Authenticated admin requests without a valid authentication context receive 401; authenticated non-Administrator users receive 403. Error body for framework JWT challenge/forbid is not explicitly customized here. Rate limit rejection status is 429 (`ApiError` body is not explicitly set by the limiter). The app may return 500 for unhandled database/configuration faults. `409` and `422` should not be expected from these endpoints based on current code.

## Example requests

Create an inactive external campaign:

```http
POST /api/admin/hiring-drives
Authorization: Bearer <admin-token>
Content-Type: application/json
```

```json
{
  "title": "Software hiring drive",
  "startAtUtc": "2026-10-22T00:00:00Z",
  "companyId": "fa29151f-7372-4e59-a19a-6dc18f66e516",
  "subtitle": "Graduate Engineer",
  "description": "Verified details supplied by the administrator.",
  "badgeText": "Hiring Now",
  "badgeType": "hiring",
  "imageUrl": null,
  "locationText": "Bengaluru",
  "experienceText": "0-1 year",
  "jobId": null,
  "applyUrl": "https://careers.example.test/apply",
  "endAtUtc": null,
  "displayOrder": 0,
  "isActive": false,
  "isSponsored": false
}
```

Track visible cards:

```http
POST /api/public/hiring-drives/impressions
Content-Type: application/json
```

```json
{ "driveIds": ["5c0911bb-b080-4f40-b461-76993d889ba1"] }
```

## Frontend integration notes

- Use the configured API origin and `/api` base path. Public controller is `[AllowAnonymous]`; no bearer token is required for public list or analytics.
- Public endpoint is CORS-enabled only for configured `Cors:AllowedOrigins` in deployed environments. Development also allows `http://localhost:5173` and `http://localhost:4173`. A frontend production origin must be added to that server configuration or browsers will block cross-origin reads. Credentials are allowed by the default CORS policy. This source cannot establish whether a particular deployed frontend origin is currently configured.
- Public JSON is inside `data`; map `response.data`. Admin list is `response.data.items`; pagination fields are in that same data object.
- Call impressions when cards become visible, not merely after fetching. A batch may include up to 50 unique IDs. 204 has no body.
- On apply, POST click, then use `response.data.destinationType`; for `internalJob` navigate directly using the returned authoritative `jobSlug` (retain `jobId` as stable identity), and for `external` navigate to returned `url`. Do not perform JobId-to-search-to-slug resolution or use a client-supplied destination.
- Respect 429 and retry with backoff. Analytics count accepted events, not unique viewers/clickers. Rate limit is in-memory per API process and IP based for feature writes.
- Render title, subtitle, badge, location, experience and description as text, never as HTML.

## TypeScript types

```typescript
export interface ApiResponse<T> {
  data: T;
  message?: string | null;
}

export interface HiringDriveCompanyDto {
  id: string;
  name: string;
  logoUrl: string | null;
}

export type HiringDriveBadgeType = "hiring" | "walkIn" | "openings" | "featured";
export type HiringDriveDestinationType = "internalJob" | "external";

export type HiringDriveDestinationDto =
  | { type: "internalJob"; jobId: string; url?: never }
  | { type: "external"; url: string; jobId?: never };

export interface HiringDrivePublicDto {
  id: string;
  title: string;
  subtitle: string | null;
  badgeText: string | null;
  badgeType: HiringDriveBadgeType | null;
  company: HiringDriveCompanyDto | null;
  imageUrl: string | null;
  locationText: string | null;
  experienceText: string | null;
  isSponsored: boolean;
  destination: HiringDriveDestinationDto;
}

export type HiringDriveClickResponse =
  | { destinationType: "internalJob"; jobId: string; jobSlug: string; url?: never }
  | { destinationType: "external"; url: string; jobId?: never };

export interface HiringDriveClickEnvelope extends ApiResponse<HiringDriveClickResponse> {}

export interface HiringDriveImpressionsRequest {
  driveIds: string[];
}

export interface HiringDriveAdminCampaign {
  title: string;
  startAtUtc: string;
  companyId?: string | null;
  subtitle?: string | null;
  description?: string | null;
  badgeText?: string | null;
  badgeType?: HiringDriveBadgeType | null;
  imageUrl?: string | null;
  locationText?: string | null;
  experienceText?: string | null;
  jobId?: string | null;
  applyUrl?: string | null;
  endAtUtc?: string | null;
  displayOrder?: number;
  isActive?: boolean;
  isSponsored?: boolean;
}

export type CreateHiringDriveRequest = HiringDriveAdminCampaign;
export type UpdateHiringDriveRequest = HiringDriveAdminCampaign;

export interface HiringDriveAdminDto {
  id: string;
  campaign: HiringDriveAdminCampaign;
  impressionCount: number;
  clickCount: number;
  ctr: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  createdByUserId: string | null;
  updatedByUserId: string | null;
}

export interface PagedResponse<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
```
