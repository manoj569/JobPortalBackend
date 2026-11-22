# Hiring Outreach Data Protection operations

The application uses one `CareerHarbor` Data Protection registration. When Hiring Outreach
is enabled in Production, the official EF repository stores encrypted key XML in
`DataProtectionKeys` through `JobPortalDbContext` and the existing PostgreSQL
`ConnectionStrings__DefaultConnection`. The separate additive `AddDataProtectionKeyPersistence`
migration `20261122213025_AddDataProtectionKeyPersistence` must be reviewed and applied
by the deployment owner before activation. Its Up creates only the standard key table;
its Down removes that table. Do not roll it back after protecting credentials without
a reviewed key backup/recovery plan.
This implementation does not apply production migrations or change Render settings.

`HiringOutreach__DataProtection__CertificateBase64` contains a password-protected PFX;
`HiringOutreach__DataProtection__CertificatePassword` unlocks it. The loader uses ephemeral
key storage, never a certificate file. Startup requires valid PostgreSQL configuration,
a currently valid RSA private key of at least 2048 bits, and KeyEncipherment usage when
a key-usage extension is present. Configuration errors omit certificate material and
passwords. Failed database key writes cannot fall back to plaintext database persistence.
Database reachability and table availability are verified when keys are read/written;
configuration validation alone is not a successful database connectivity probe.

## Generate secrets locally

Use PowerShell 7. Generate and save a unique random password of at least 32 characters
in a password manager, then run from the backend repository:

```powershell
pwsh -NoProfile -File .\scripts\New-CareerHarborDataProtectionCertificate.ps1
```

Paste the saved password at the hidden prompt. The only output is the secret Base64 PFX.
Put that output directly into Render's `HiringOutreach__DataProtection__CertificateBase64`
secret and the saved password into `HiringOutreach__DataProtection__CertificatePassword`.
Never paste either into chat, source, tickets or logs. The helper writes no files or
certificate-store entries. It creates a 3072-bit RSA self-signed certificate valid for
five years. Do not run it again on every deployment.

Keep the same certificate, password, application name, key rows and protection purposes
across deployments. Back up the certificate/password securely and independently of the
database. The same application name alone cannot recover ciphertext if its original
key ring or decrypting certificate is lost. Do not delete key rows or replace/rotate the
certificate without an explicit old-key decryption and retention plan.

Remove obsolete Hiring Outreach `DataProtectionKeysPath`, `DataProtectionCertificatePath`
and `DataProtectionCertificatePassword` variables under `HiringOutreach__DataProtection__`.
Retain the normal database connection and existing Hunter/Google feature settings. No
Hunter, Gmail OAuth/send, protection purpose or token-format change is part of this fix.
Keep production Hiring Outreach disabled until the owner performs deployment checks.

## AIApply compatibility

With Hiring Outreach disabled, explicitly configured AIApply ExternalSessions filesystem
storage remains unchanged. Enabling shared PostgreSQL storage while ExternalSessions has
legacy file/certificate settings fails with a migration instruction. Preserve/import the
existing key XML and its original private certificate through separately reviewed tooling
before removing those settings; this task does not automatically copy or discard keys.

After that migration, ExternalSessions validation accepts the verified shared repository
and its health check performs a protected round trip rather than requiring a directory.
If ExternalSessions remains enabled and the new certificate settings remain configured,
disabling Hiring Outreach retains the shared database ring. Never remove those settings
while another consumer needs it. Existing login/session ciphertext also requires its
original keys; switching repositories does not by itself migrate those keys.

Ordinary tests and development without a PFX keep development defaults, and never store
plaintext key XML in the shared database. This convenience is not production durability.

## Verification boundary

Integration tests opt in only to disposable localhost PostgreSQL `hiring_outreach_verify`
on port 55439 via `HIRING_DP_TEST_POSTGRES`; no Neon connection is used. They verify
encrypted key rows and Gmail credential decryption after recreating the service provider.
Actual Render startup, secret injection, live Gmail and Hunter remain deployment-owner checks.

Verified locally on 2026-11-23: solution build (including the generated migration) completed
with zero warnings/errors; EF reported no pending model changes. The migration applied
only to disposable `hiring_outreach_verify`. Data Protection focused tests: 24 passed,
0 failed, 0 skipped. Existing Hiring Outreach/provider tests: 41 passed, 0 failed,
0 skipped. Full relevant Hiring/OAuth/authentication/AIApply/external-session/career-session
and dependency-registration regression: 380 passed, 0 failed, 8 skipped (388 total).
Those eight are existing opt-in PostgreSQL fixtures for CareerSession, distributed AIApply
and HiringDrive; they were not configured in this run. This is not full backend or live
production regression certification. TRX files are in the test project's TestResults folder.
