#Requires -Version 7.0
<#
Run locally, never on Render. Generate/save a unique 32+ character RANDOM password in
your password manager first. Paste it into the hidden prompt. The sole output is the
Base64 password-protected PFX, which is also a SECRET: store it securely and paste it
only into Render's secret environment settings, never source control, logs or chat.
No certificate or PFX file is written and no certificate-store entry is created.
#>
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$dpPassword=Read-Host 'Paste the random 32+ character password saved in your password manager' -AsSecureString
if($dpPassword.Length -lt 32) { $dpPassword.Dispose(); throw 'Use a unique random password of at least 32 characters.' }
$dpRsa=$null
$dpCertificate=$null
$dpPfxBytes=$null
try {
    $dpRsa=[System.Security.Cryptography.RSA]::Create(3072)
    $dpRequest=[System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=CareerHarbor Data Protection', $dpRsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $dpUsage=[System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment -bor [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DataEncipherment
    $dpRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($dpUsage,$true))
    $dpCertificate=$dpRequest.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddYears(5))
    $dpPfxBytes=$dpCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$dpPassword)
    [Convert]::ToBase64String($dpPfxBytes)
} finally {
    if($null -ne $dpPfxBytes) { [Array]::Clear($dpPfxBytes,0,$dpPfxBytes.Length) }
    if($null -ne $dpCertificate) { $dpCertificate.Dispose() }
    if($null -ne $dpRsa) { $dpRsa.Dispose() }
    $dpPassword.Dispose()
}
