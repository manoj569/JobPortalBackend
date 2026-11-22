using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using JobPortal.API.Health;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIApply;
using JobPortal.Infrastructure.HiringOutreach;
using JobPortal.Persistence;
using JobPortal.Persistence.Context;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class HiringDataProtectionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfiguredOutreachSelectsOnlyEncryptedDatabaseRepository(bool production)
    {
        var bundle=Certificate(); var config=Configuration(bundle);
        var forbiddenPath=Path.Combine(Path.GetTempPath(),"dp-must-not-create-"+Guid.NewGuid().ToString("N"));
        config["HiringOutreach:DataProtection:DataProtectionKeysPath"]=forbiddenPath;
        config["HiringOutreach:DataProtection:DataProtectionCertificatePath"]="must-not-read.pfx";
        var services=Services(new InMemoryDatabaseRoot(),Guid.NewGuid().ToString());
        var state=services.AddCareerHarborDataProtection(config,production);
        using var provider=services.BuildServiceProvider();
        Assert.True(state.UsesProtectedDatabaseKeyRing);
        var keys=provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.IsType<EntityFrameworkCoreXmlRepository<JobPortalDbContext>>(keys.XmlRepository);
        Assert.IsType<CertificateXmlEncryptor>(keys.XmlEncryptor);
        Assert.Equal("CareerHarbor",provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        Assert.False(Directory.Exists(forbiddenPath));
    }
    [Theory]
    [InlineData("missing-both")]
    [InlineData("missing-password")]
    [InlineData("missing-certificate")]
    [InlineData("missing-connection")]
    [InlineData("invalid-connection")]
    public void ProductionMissingConfigurationFailsWithSafeMessage(string problem)
    {
        var config=Configuration(Certificate());
        if(problem is "missing-both" or "missing-certificate") config["HiringOutreach:DataProtection:CertificateBase64"]=null;
        if(problem is "missing-both" or "missing-password") config["HiringOutreach:DataProtection:CertificatePassword"]=null;
        if(problem=="missing-connection") config["ConnectionStrings:DefaultConnection"]=null;
        if(problem=="invalid-connection") config["ConnectionStrings:DefaultConnection"]="Port=invalid;Password=never-echo";
        var error=Assert.Throws<InvalidOperationException>(()=>new ServiceCollection().AddCareerHarborDataProtection(config,true));
        Assert.True(error.Message==CareerHarborDataProtectionRegistration.ConfigurationError);
        Assert.Null(error.InnerException);
    }
    [Theory]
    [InlineData("base64")]
    [InlineData("password")]
    [InlineData("pfx")]
    [InlineData("public-only")]
    [InlineData("ecdsa")]
    [InlineData("expired")]
    [InlineData("usage")]
    [InlineData("weak-rsa")]
    public void InvalidCertificatesFailWithoutLeakingMaterial(string problem)
    {
        var config=Configuration(Certificate(problem));
        if(problem=="base64") config["HiringOutreach:DataProtection:CertificateBase64"]="invalid-base64!";
        if(problem=="password") config["HiringOutreach:DataProtection:CertificatePassword"]="incorrect-test-password";
        if(problem=="pfx") config["HiringOutreach:DataProtection:CertificateBase64"]=Convert.ToBase64String(Encoding.UTF8.GetBytes("not-a-pfx"));
        var error=Assert.Throws<InvalidOperationException>(()=>new ServiceCollection().AddCareerHarborDataProtection(config,true));
        Assert.True(error.Message==CareerHarborDataProtectionRegistration.CertificateError); Assert.Null(error.InnerException);
    }
    [Fact]
    public void DisabledOutreachDoesNotRequireOrLoadItsCertificateInProduction()
    {
        var config=Configuration(null); config["HiringOutreach:Enabled"]="false";
        config["HiringOutreach:DataProtection:CertificateBase64"]="invalid-unused-value";
        var state=new ServiceCollection().AddCareerHarborDataProtection(config,true);
        Assert.False(state.UsesProtectedDatabaseKeyRing);
    }
    [Fact]
    public void DevelopmentWithoutCertificateDoesNotPersistPlaintextKeysIntoDatabase()
    {
        var services=Services(new InMemoryDatabaseRoot(),Guid.NewGuid().ToString());
        Assert.False(services.AddCareerHarborDataProtection(Configuration(null),false).UsesProtectedDatabaseKeyRing);
        using var provider=services.BuildServiceProvider();
        Assert.IsNotType<EntityFrameworkCoreXmlRepository<JobPortalDbContext>>(provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
    }
    [Fact]
    public void LegacyExternalSessionFileRepositoryIsPreservedWhenOutreachDisabled()
    {
        var path=Path.Combine(Path.GetTempPath(),"dp-legacy-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var config=Configuration(null); config["HiringOutreach:Enabled"]="false";
            config["AIApply:ExternalSessions:Enabled"]="true"; config["AIApply:ExternalSessions:DataProtectionKeysPath"]=path;
            var services=new ServiceCollection(); services.AddLogging();
            Assert.False(services.AddCareerHarborDataProtection(config,true).UsesProtectedDatabaseKeyRing);
            using var provider=services.BuildServiceProvider();
            Assert.IsType<Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository>(provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
            Assert.True(Directory.Exists(path));
        }
        finally { if(Directory.Exists(path)) Directory.Delete(path,false); }
    }
    [Fact]
    public void EnablingBothFeaturesNeverSilentlyReplacesLegacyKeys()
    {
        var config=Configuration(Certificate()); config["AIApply:ExternalSessions:Enabled"]="true";
        config["AIApply:ExternalSessions:DataProtectionKeysPath"]="existing-ring";
        var error=Assert.Throws<InvalidOperationException>(()=>new ServiceCollection().AddCareerHarborDataProtection(config,true));
        Assert.True(error.Message==CareerHarborDataProtectionRegistration.LegacyRepositoryError);
    }
    [Fact]
    public async Task SharedDatabaseRingSatisfiesExternalSessionValidationAndHealthWithoutAFilePath()
    {
        var services=Services(new InMemoryDatabaseRoot(),Guid.NewGuid().ToString());
        var config=Configuration(Certificate()); config["AIApply:ExternalSessions:Enabled"]="true";
        var state=services.AddCareerHarborDataProtection(config,true);
        using var provider=services.BuildServiceProvider();
        var settings=new AIApplyOptions(); settings.ExternalSessions.Enabled=true;
        Assert.True(new AIApplyOptionsValidator(false,state.UsesProtectedDatabaseKeyRing).Validate(null,settings).Succeeded);
        Assert.True(new AIApplyOptionsValidator(false).Validate(null,settings).Failed);
        var check=new AIApplyExternalSessionHealthCheck(Options.Create(settings),state,provider.GetRequiredService<IDataProtectionProvider>());
        Assert.Equal(HealthStatus.Healthy,(await check.CheckHealthAsync(new())).Status);
        var unavailable=new AIApplyExternalSessionHealthCheck(Options.Create(settings),state);
        Assert.Equal(HealthStatus.Unhealthy,(await unavailable.CheckHealthAsync(new())).Status);
    }
    [Fact]
    public void ExistingSharedDatabaseExternalSessionsContinueWhenOutreachIsDisabled()
    {
        var config=Configuration(Certificate()); config["HiringOutreach:Enabled"]="false";
        config["AIApply:ExternalSessions:Enabled"]="true";
        Assert.True(new ServiceCollection().AddCareerHarborDataProtection(config,true).UsesProtectedDatabaseKeyRing);
    }
    [Fact]
    public void DatabaseWriteFailureNeverFallsBackToAnUnpersistedOrPlaintextKeyRing()
    {
        var services=new ServiceCollection(); services.AddLogging();
        services.AddDbContext<JobPortalDbContext>(o=>o.UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new RejectKeyWrites()));
        services.AddCareerHarborDataProtection(Configuration(Certificate()),true);
        using var provider=services.BuildServiceProvider();
        var protector=provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("failure-proof");
        Assert.Throws<CryptographicException>(()=>protector.Protect("fake-credential"));
    }
    [Fact]
    public void RecreatedProvidersDecryptGmailAndExternalSessionsWithSameCertificateAndRing()
    {
        var root=new InMemoryDatabaseRoot(); var name=Guid.NewGuid().ToString(); var config=Configuration(Certificate());
        var owner=Guid.NewGuid(); var original="fake-refresh-"+Guid.NewGuid().ToString("N"); string cipher; byte[] external;
        using(var first=Provider(root,name,config))
        {
            var protection=first.GetRequiredService<IDataProtectionProvider>();
            cipher=new HiringCredentialProtector(protection).Protect(owner,original);
            external=new ExternalJobSiteSessionProtector(protection).Protect(owner,JobSiteIdentifier.Workday,Encoding.UTF8.GetBytes("{\"cookies\":[]}"));
            using var scope=first.CreateScope(); var rows=scope.ServiceProvider.GetRequiredService<JobPortalDbContext>().DataProtectionKeys.ToArray();
            Assert.NotEmpty(rows); Assert.All(rows,AssertEncrypted);
        }
        using var second=Provider(root,name,config);
        var restarted=second.GetRequiredService<IDataProtectionProvider>();
        Assert.Equal(original,new HiringCredentialProtector(restarted).Unprotect(owner,cipher));
        Assert.Equal("{\"cookies\":[]}",Encoding.UTF8.GetString(new ExternalJobSiteSessionProtector(restarted).Unprotect(owner,JobSiteIdentifier.Workday,external)));
        Assert.Throws<CryptographicException>(()=>new HiringCredentialProtector(restarted).Unprotect(Guid.NewGuid(),cipher));
    }
    [DataProtectionPostgresFact]
    public void PostgresPersistsEncryptedKeysAndFreshProviderDecryptsPreviouslyProtectedGmailCredential()
    {
        var connection=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HIRING_DP_TEST_POSTGRES"));
        Assert.Equal("127.0.0.1",connection.Host); Assert.Equal(55439,connection.Port); Assert.Equal("hiring_outreach_verify",connection.Database);
        var config=Configuration(Certificate()); config["ConnectionStrings:DefaultConnection"]=connection.ConnectionString;
        var owner=Guid.NewGuid(); var original="fake-gmail-refresh-"+Guid.NewGuid().ToString("N"); string cipher; int[] storedIds;
        using(var first=PostgresProvider(config,connection.ConnectionString))
        {
            cipher=new HiringCredentialProtector(first.GetRequiredService<IDataProtectionProvider>()).Protect(owner,original);
            using var scope=first.CreateScope(); var rows=scope.ServiceProvider.GetRequiredService<JobPortalDbContext>().DataProtectionKeys.AsNoTracking().ToArray();
            Assert.NotEmpty(rows); Assert.All(rows,AssertEncrypted); storedIds=rows.Select(x=>x.Id).ToArray();
        }
        using var second=PostgresProvider(config,connection.ConnectionString);
        Assert.Equal(original,new HiringCredentialProtector(second.GetRequiredService<IDataProtectionProvider>()).Unprotect(owner,cipher));
        using var check=second.CreateScope(); var persisted=check.ServiceProvider.GetRequiredService<JobPortalDbContext>().DataProtectionKeys.Select(x=>x.Id).ToArray();
        Assert.All(storedIds,id=>Assert.Contains(id,persisted));
    }
    private static void AssertEncrypted(DataProtectionKey row)
    {
        // Boolean assertions deliberately never dump key XML into a failing test report.
        Assert.True(row.Id>0); Assert.False(string.IsNullOrWhiteSpace(row.FriendlyName));
        Assert.True(row.Xml?.Contains("encryptedSecret",StringComparison.Ordinal)==true);
        Assert.False(row.Xml?.Contains("<masterKey",StringComparison.Ordinal)==true);
    }
    private static ServiceCollection Services(InMemoryDatabaseRoot root,string name)
    { var services=new ServiceCollection(); services.AddLogging(); services.AddDbContext<JobPortalDbContext>(o=>o.UseInMemoryDatabase(name,root)); return services; }
    private static ServiceProvider Provider(InMemoryDatabaseRoot root,string name,IConfiguration config)
    { var services=Services(root,name); services.AddCareerHarborDataProtection(config,true); return services.BuildServiceProvider(); }
    private static ServiceProvider PostgresProvider(IConfiguration config,string connection)
    {
        var services=new ServiceCollection(); services.AddLogging();
        services.AddDbContext<JobPortalDbContext>(o=>o.UseNpgsql(connection,p=>p.EnableRetryOnFailure(3)));
        services.AddCareerHarborDataProtection(config,true); return services.BuildServiceProvider();
    }
    private static IConfigurationRoot Configuration((string Encoded,string Password)? bundle)
    {
        var values=new Dictionary<string,string?> { ["HiringOutreach:Enabled"]="true",["ConnectionStrings:DefaultConnection"]="Host=127.0.0.1;Database=dp_model;Username=postgres" };
        if(bundle.HasValue) { values["HiringOutreach:DataProtection:CertificateBase64"]=bundle.Value.Encoded; values["HiringOutreach:DataProtection:CertificatePassword"]=bundle.Value.Password; }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private static (string Encoded,string Password) Certificate(string kind="valid")
    {
        var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var rsa=RSA.Create(kind=="weak-rsa" ? 1024 : 2048);
        using var ec=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request=kind=="ecdsa" ? new CertificateRequest("CN=Ephemeral test",ec,HashAlgorithmName.SHA256) :
            new CertificateRequest("CN=Ephemeral test",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(kind=="usage" ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment,true));
        using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2),kind=="expired" ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddDays(7));
        using var publicOnly=kind=="public-only" ? X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert)) : null;
        var pfx=(publicOnly ?? cert).Export(X509ContentType.Pfx,password);
        try { return (Convert.ToBase64String(pfx),password); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
    private sealed class DataProtectionPostgresFactAttribute : FactAttribute
    {
        public DataProtectionPostgresFactAttribute()
        { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HIRING_DP_TEST_POSTGRES"))) Skip="Requires explicitly disposable localhost hiring_outreach_verify PostgreSQL."; }
    }
    private sealed class RejectKeyWrites : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,InterceptionResult<int> result)=>
            throw new InvalidOperationException("Simulated key persistence failure.");
    }
}
