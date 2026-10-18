# Registration Without Email Verification

## Old registration flow

`POST /api/auth/register` calls `AuthService.RegisterAsync`. Registration created an active Candidate with `EmailConfirmed = false`, generated a verification token and expiry, and enqueued a `RegistrationEmailRequest`. The background registration email dispatcher sent the verification email. `POST /api/auth/login` checked the password and then returned `email_verification_required` when `EmailConfirmed` was false.

## New registration flow

Registration now creates a Candidate with `EmailConfirmed = true`, no verification-token hash or expiry, and no registration email outbox entry. The account is persisted through the same transaction and can immediately pass the existing login verification invariant. The success response remains the existing `RegistrationResponse` with message `Registration successful. Please log in.` It does not ask the user to check email. The response DTO and properties are unchanged.

The general login check remains unchanged. Therefore, pre-existing unverified users still receive the existing `email_verification_required` response. No existing user data is updated.

## Exact code change

In `AuthService.RegisterAsync`, removed the verification-token generation and registration outbox enqueue. New registrations explicitly store `EmailConfirmed = true` and null verification token fields. Removed the now-unneeded registration outbox dependency from `AuthService`; the outbox repository, hosted dispatcher, verification email method/template, token validation, verification endpoint, and database schema remain in place. Password-reset email logic was not changed.

## Email behavior and regression coverage

- Registration does not enqueue a verification email. It does not call the email provider.
- Forgot Password after registration still uses the existing password-reset token hash, expiry, persistence, and `SendPasswordResetAsync` path. The new test covers requesting reset, completing reset, and logging in with the new password.
- Verification endpoint and background dispatcher tests now use a historical unverified user / queued request, so they continue to cover the preserved capability without implying that new registrations queue email.

## Files changed

- `JobPortal.Application/Features/Authentication/AuthService.cs`
- `JobPortal.Application.Tests/AuthenticationTests.cs`

No frontend files were changed. No database migration or existing-user data update was made.

## Tests and validation

- Application build: **passed**, 0 warnings and 0 errors.
- API build: **passed**, 0 warnings and 0 errors.
- Focused authentication test assembly build: **passed** with analyzers disabled for the test invocation.
- Focused authentication test execution: **not completed**. VSTest printed “Starting test execution” but did not connect to or launch a visible testhost during an extended wait; execution was stopped. No test results are claimed.
- Forgot Password, login, registration, and verification tests are present in the focused group, but none were executed because testhost startup did not complete.
- `git diff --check`: **passed**.
- `git status`: only the two files listed above are modified; this report is new.

## Frontend handoff

For password registration, remove any UI step, copy, or success state that tells the user to check email or verify before signing in. The backend still returns the existing message, “Registration successful. Please log in.” The registration endpoint and response contract have not changed. Keep separate email-related flows such as Forgot Password unchanged.

## Confirmation and remaining risk

- Migration required: **NO**.
- Registration verification email sent: **NO** (no registration outbox entry is created).
- New user immediately login eligible: **YES**, through the persisted `EmailConfirmed = true` state and unchanged login path.
- Forgot Password email preserved: **YES** in code; test added but execution remains unverified due testhost startup.
- Verification infrastructure preserved: **YES**.
- Remaining risk: because the testhost could not start, the new and existing authentication tests still need an executable test run. Email ownership is no longer confirmed during password registration; this is the requested product behavior.
