using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using JobPortal.Persistence.Context;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JobPortal.Persistence;

// A server-created capability, not a request/configuration flag that can bypass validation.
public sealed record CareerHarborDataProtectionState(bool UsesProtectedDatabaseKeyRing);

public static class CareerHarborDataProtectionRegistration
{
    public const string ConfigurationError="Hiring outreach requires protected persistent Data Protection configuration in production.";
    public const string CertificateError="Hiring outreach Data Protection certificate configuration is invalid.";
    public const string LegacyRepositoryError="Migrate the existing AIApply Data Protection key ring before switching to shared PostgreSQL persistence; remove legacy filesystem settings only after preserving existing keys and their certificate.";

    public static CareerHarborDataProtectionState AddCareerHarborDataProtection(this IServiceCollection services,IConfiguration configuration,bool isProduction)
    {
        var builder=services.AddDataProtection().SetApplicationName("CareerHarbor");
        var hiring=bool.TryParse(configuration["HiringOutreach:Enabled"],out var hiringEnabled) && hiringEnabled;
        var settings=configuration.GetSection("HiringOutreach:DataProtection");
        var encoded=settings["CertificateBase64"]; var password=settings["CertificatePassword"];
        var legacy=configuration.GetSection("AIApply:ExternalSessions");
        var legacyEnabled=bool.TryParse(legacy["Enabled"],out var externalEnabled) && externalEnabled;
        var legacyConfigured=!string.IsNullOrWhiteSpace(legacy["DataProtectionKeysPath"]) ||
            !string.IsNullOrWhiteSpace(legacy["DataProtectionCertificatePath"]) || !string.IsNullOrWhiteSpace(legacy["DataProtectionCertificatePassword"]);
        var certificateConfigured=!string.IsNullOrWhiteSpace(encoded) || !string.IsNullOrWhiteSpace(password);
        // A still-enabled ExternalSessions consumer can keep the shared DB ring when outreach is
        // disabled. An existing explicit filesystem configuration always requires planned migration.
        var sharedDatabaseRequested=hiring || legacyEnabled && !legacyConfigured && certificateConfigured;
        var usesDatabase=false;
        if(sharedDatabaseRequested && (isProduction || certificateConfigured))
        {
            if(string.IsNullOrWhiteSpace(encoded) || string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException(ConfigurationError);
            ValidatePostgres(configuration.GetConnectionString("DefaultConnection"));
            // Never silently abandon a file key ring or configure competing XML repositories.
            if(legacyEnabled && legacyConfigured)
                throw new InvalidOperationException(LegacyRepositoryError);
            var certificate=LoadCertificate(encoded,password);
            // Keep the certificate alive with the application key-ring configuration. All key XML
            // serialization, persistence, envelope encryption and decryption use official APIs.
            builder.PersistKeysToDbContext<JobPortalDbContext>()
                .ProtectKeysWithCertificate(certificate)
                .UnprotectKeysWithAnyCertificate(certificate);
            usesDatabase=true;
        }
        else if(legacyEnabled)
        {
            // Preserve legacy ExternalSessions behavior when shared DB persistence isn't selected.
            var keysPath=legacy["DataProtectionKeysPath"];
            if(!string.IsNullOrWhiteSpace(keysPath)) builder.PersistKeysToFileSystem(Directory.CreateDirectory(keysPath));
            var path=legacy["DataProtectionCertificatePath"]; var legacyPassword=legacy["DataProtectionCertificatePassword"];
            if(!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(legacyPassword))
                builder.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(path,legacyPassword,X509KeyStorageFlags.EphemeralKeySet));
        }
        // Development without certificate configuration retains ordinary development defaults.
        // It never writes unencrypted XML into the shared database. Production never takes this
        // branch when HiringOutreach is enabled; ordinary unrelated tests need no PFX.
        var state=new CareerHarborDataProtectionState(usesDatabase);
        services.AddSingleton(state);
        return state;
    }
    private static void ValidatePostgres(string? connection)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException(ConfigurationError);
            var parsed=new NpgsqlConnectionStringBuilder(connection);
            if(string.IsNullOrWhiteSpace(parsed.Host) || string.IsNullOrWhiteSpace(parsed.Database)) throw new InvalidOperationException(ConfigurationError);
        }
        catch(Exception ex) when(ex is ArgumentException or FormatException)
        { throw new InvalidOperationException(ConfigurationError); }
    }
    private static X509Certificate2 LoadCertificate(string encoded,string password)
    {
        byte[]? bytes=null; X509Certificate2? certificate=null;
        try
        {
            if(encoded.Length>1024*1024) throw new InvalidOperationException(CertificateError);
            bytes=Convert.FromBase64String(encoded);
            certificate=X509CertificateLoader.LoadPkcs12(bytes,password,X509KeyStorageFlags.EphemeralKeySet);
            using var privateKey=certificate.GetRSAPrivateKey();
            using var publicKey=certificate.GetRSAPublicKey();
            var usage=certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
            if(!certificate.HasPrivateKey || privateKey is null || publicKey is null || privateKey.KeySize<2048 ||
                certificate.NotBefore.ToUniversalTime()>DateTime.UtcNow || certificate.NotAfter.ToUniversalTime()<=DateTime.UtcNow ||
                usage is not null && !usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment))
                throw new InvalidOperationException(CertificateError);
            return certificate;
        }
        catch(Exception ex) when(ex is CryptographicException or FormatException or ArgumentException or InvalidOperationException)
        {
            certificate?.Dispose();
            // No inner exception or configuration value: neither PFX nor password reaches logs.
            throw new InvalidOperationException(CertificateError);
        }
        finally { if(bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }
}
