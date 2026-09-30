# Google account password access

## Audit and implementation

Google registration stores `User.PasswordHash = null`, `EmailConfirmed = true`, and an existing `UserExternalLogin` whose provider is `ExternalLoginProvider.Google` (numeric value 1). Local password presence uses the same non-empty-hash convention as existing login/account settings. A linked provider alone does not imply Google-only: accounts with a password retain normal registration and reset behavior.

The cause of missing forgot-password emails was `AuthService.RequestPasswordResetAsync` explicitly returning before sending when PasswordHash was empty. Active Google-linked passwordless users now pass this gate. Passwordless accounts without a Google link and inactive accounts remain ineligible, with the same public response.

Registration reuses the normalized email lookup (`Trim().ToLowerInvariant()`), existing external-login repository, and existing unique-conflict recovery. Google-only duplicate accounts get guidance both during initial lookup and after a concurrent registration wins. No additional user or external-login record is created by this path. Existing password/email/phone conflict codes and the existing soft-delete uniqueness policy are unchanged.

Google sign-in code was not changed. It validates the Google credential for the configured audience, requires a verified email and valid subject, looks up provider subject and normalized email, rejects identity collisions, and restricts linking to eligible Candidate accounts. Only that already-validated flow may link an existing local account; this password-access change performs no account linking.

Login deliberately retains the generic `unauthorized` response for absent users, Google-only accounts, inactive accounts and wrong passwords. It does not expose `GOOGLE_SIGN_IN_REQUIRED` or a provider lookup result. Registration already reveals duplicates, but that is not a reason to add provider enumeration to login.

## API contracts for the frontend

`POST /api/auth/register`, Google-only duplicate, HTTP 409:

```json
{
  "code": "ACCOUNT_EXISTS_GOOGLE",
  "message": "This email is already registered with Google. Please continue with Google to sign in.",
  "errors": null,
  "loginMethod": "google"
}
```

Password-account duplicates keep HTTP 409 and existing `registration_email_exists`, `registration_identity_exists` or `registration_phone_exists`. Their envelope remains `{ "code": "...", "message": "...", "errors": null }`. `loginMethod` is omitted on ordinary errors, preserving existing response shape. Google + password accounts use the existing password-account conflict behavior.

`POST /api/auth/login` keeps `{ "identifier": "...", "password": "..." }`. Invalid credentials return HTTP 401:

```json
{ "code": "unauthorized", "message": "Invalid identifier or password.", "errors": null }
```

`POST /api/auth/request-password-reset` with `{ "email": "..." }` returns HTTP 202 for unknown, eligible password, Google-only, and Google + password accounts:

```json
{ "message": "If an account exists for this email address, a password reset link has been sent." }
```

`POST /api/auth/complete-password-reset` accepts `{ "token": "...", "newPassword": "..." }` for both setup and reset. Success, HTTP 200:

```json
{ "message": "Password changed successfully. Please log in." }
```

Invalid/expired/used tokens return HTTP 400 with `code: "invalid_password_reset"`. Password validation errors continue to use `validation_error`. No old password is required. Valid reset updates the existing Users.Id, clears the reset token, revokes existing refresh sessions and leaves all Google mappings intact. Existing Google authentication and password login both remain available after setup.

### Frontend work required afterward (not implemented here)

1. On registration `ACCOUNT_EXISTS_GOOGLE`, display the server guidance and a Continue with Google action using the existing Google login flow. Do not attempt another registration.
2. Preserve handling of the existing password/phone conflict codes; do not require a new `ACCOUNT_EXISTS_PASSWORD` code.
3. Keep login errors generic. Offer Continue with Google and Forgot Password generally, not based on a new account-discovery API.
4. Allow Google-only users to use the existing Forgot Password screen; always show the generic request result.
5. Make `/reset-password?token=...` wording suitable for both creating and resetting a password. Submit the existing complete-password-reset contract, then send the user to login. Do not require an old password, register again, or log tokens.

## Email, tokens and security

Existing Brevo `SendPasswordResetAsync` is reused, with a Create your CareerHarbor password subject and create-password wording when no local hash exists. Password users retain the existing Reset your Career Portal password wording. The setup copy is intentionally provider-neutral rather than assuming every currently linked account originally registered via Google. The URL remains the existing reset-password route, with a URL-encoded token and no email address in its query.

The existing token is 32 cryptographically random bytes, base64url encoded; only its SHA-256 hash and 30-minute expiry are stored on the intended user. Reset checks active status, expiry and constant-time hash equality, then clears the token. Sequential reuse is rejected. Existing PBKDF2-SHA512 password hashing (210,000 iterations, random salt) and password policy are unchanged. Tokens/passwords are not added to logs. This change does not add a new claim of atomic concurrent token consumption or constant-time account discovery; the existing synchronous email/timing and reset persistence architecture are retained.

Existing IP rate limits remain: authentication 10/minute, Google authorization-code authentication 5/minute, reset requests 5/5 minutes, reset completion 10/5 minutes. No JWT, refresh, logout, verification, admin, terms/privacy, or authorization policy was changed. Email is attempted through the existing service, not a new durable queue; delivery still requires enabled, correctly configured Brevo settings.

No entity, mapping, snapshot or migration change is needed. No database was accessed for this implementation.

## Changed files

- `JobPortal.Application/Features/Authentication/AuthService.cs`
- `JobPortal.Application/Common/Exceptions/AppException.cs`
- `JobPortal.API/Middleware/GlobalExceptionMiddleware.cs`
- `JobPortal.Shared/Models/ApiError.cs`
- `JobPortal.Infrastructure/Services/BrevoEmailService.cs`
- `JobPortal.Application.Tests/AuthenticationTests.cs`
- `JobPortal.Application.Tests/GoogleAuthenticationTests.cs`
- `JobPortal.Application.Tests/BrevoEmailServiceTests.cs`
- `JobPortal.Application.Tests/GoogleRegistrationResponseTests.cs`
- `docs/google-password-access.md`

Existing tests cover ordinary registration/password reset, verification, invalid/expired tokens, refresh/logout, provider identity validation, secure linking, repeated Google sign-in and duplicate-registration races. Added tests cover Google-only and dual-method conflicts, password setup on the same user, preserved mappings, password login after setup, Google login after adding a password, generic responses, denied unlinked/inactive requests, concurrent registration guidance, email wording and the serialized API error envelope. Providers and email are faked; no real Google or email service is called.

## Validation results

- `dotnet build --no-restore`: blocked by Debug DLLs locked by the user's running JobPortal.API and Visual Studio (12 copy errors, 60 retry warnings). Neither process was stopped.
- `dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore`: PASS, 0 warnings, 0 errors.
- Test assembly Release build with `-p:RunAnalyzers=false`: PASS. The test-only flag avoids the repository's existing analyzer debt; production API analysis remained enabled.
- Authentication/Google/registration-email/Brevo/account-settings focused tests: **100 passed, 0 failed, 0 skipped, 100 total**.
- Full backend suite: **1,272 passed, 2 failed, 6 skipped, 1,280 total**.
- Existing unrelated failures: `NotificationOutboxTests.PendingSchemaDeltaIsLimitedToTheNotificationFoundation` expects an ungenerated notification delta, and `CareerTrustMigrationTests.OfflineSqlModelAndSnapshotAreConsistentWithoutUnrelatedChanges` compares an older migration model to the latest snapshot. These tests and schema files were not modified.
- Six dedicated PostgreSQL integration tests were skipped because opt-in test database configuration is absent. No production database was used.
- `git diff --check`: PASS; only Git line-ending notices.
- No staged files, commit, push, frontend edits, schema changes, migrations, database operations, or changes to unrelated untracked artifacts/documents.
