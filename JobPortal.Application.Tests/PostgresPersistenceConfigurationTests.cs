using JobPortal.Domain.Entities;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Persistence;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PostgresPersistenceConfigurationTests
{
    [Fact]
    public void ModelUsesPostgresCompatiblePartialIndexPredicates()
    {
        using var context = CreateContext();

        var user = context.Model.FindEntityType(typeof(User))
            ?? throw new InvalidOperationException("User metadata was not found.");
        var resetTokenIndex = Assert.Single(user.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(User.PasswordResetTokenHash)]));

        Assert.Equal(
            "\"PasswordResetTokenHash\" IS NOT NULL AND \"IsDeleted\" = FALSE",
            resetTokenIndex.GetFilter());
    }

    [Fact]
    public void PortfolioModelEnforcesActiveOwnershipAndSlugUniqueness()
    {
        using var context = CreateContext();
        var portfolio = context.Model.FindEntityType(typeof(CandidatePortfolio))
            ?? throw new InvalidOperationException("Portfolio metadata was not found.");
        var indexes = portfolio.GetIndexes().ToArray();
        var owner = Assert.Single(indexes, x => x.Properties.Select(y => y.Name)
            .SequenceEqual([nameof(CandidatePortfolio.UserId)]));
        var slug = Assert.Single(indexes, x => x.Properties.Select(y => y.Name)
            .SequenceEqual([nameof(CandidatePortfolio.NormalizedSlug)]));
        Assert.True(owner.IsUnique);
        Assert.True(slug.IsUnique);
        Assert.Equal("\"IsDeleted\" = FALSE", owner.GetFilter());
        Assert.Equal("\"IsDeleted\" = FALSE", slug.GetFilter());

        var experience = context.Model.FindEntityType(typeof(CandidateExperience))!;
        var relationship = Assert.Single(experience.GetForeignKeys(), x =>
            x.PrincipalEntityType.ClrType == typeof(User));
        Assert.Equal(nameof(CandidateExperience.UserId), Assert.Single(relationship.Properties).Name);
    }

    [Fact]
    public void ExternalLoginModelUsesPostgresActiveUniqueIndexes()
    {
        using var context = CreateContext();
        var externalLogin = context.Model.FindEntityType(typeof(UserExternalLogin))!;
        var indexes = externalLogin.GetIndexes().ToArray();
        var providerSubject = Assert.Single(indexes, x => x.Properties.Select(y => y.Name)
            .SequenceEqual([nameof(UserExternalLogin.Provider), nameof(UserExternalLogin.ProviderSubject)]));
        var userProvider = Assert.Single(indexes, x => x.Properties.Select(y => y.Name)
            .SequenceEqual([nameof(UserExternalLogin.UserId), nameof(UserExternalLogin.Provider)]));
        Assert.True(providerSubject.IsUnique);
        Assert.True(userProvider.IsUnique);
        Assert.Equal("\"IsDeleted\" = FALSE", providerSubject.GetFilter());
        Assert.Equal("\"IsDeleted\" = FALSE", userProvider.GetFilter());
        Assert.True(context.Model.FindEntityType(typeof(User))!
            .FindProperty(nameof(User.PasswordHash))!.IsNullable);
    }

    [Fact]
    public void ProfilePhotoUsesBoundedPostgresByteaAndUniqueCandidateOwnership()
    {
        using var context = CreateContext();
        var photo = context.Model.FindEntityType(typeof(CandidateProfilePhoto))!;
        Assert.Equal("bytea", photo.FindProperty(nameof(CandidateProfilePhoto.Content))!.GetColumnType());
        var owner = Assert.Single(photo.GetIndexes(), index => index.Properties
            .Select(property => property.Name).SequenceEqual([nameof(CandidateProfilePhoto.UserId)]));
        Assert.True(owner.IsUnique);
        Assert.Equal("\"IsDeleted\" = FALSE", owner.GetFilter());
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(photo.GetForeignKeys()).DeleteBehavior);
    }

    [Fact]
    public void ResumeDocumentBlobsArePrivateBoundedAndDurablePostgresBytea()
    {
        using var context = CreateContext();
        var blob = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ResumeDocumentBlob))!;
        Assert.Equal("ResumeDocumentBlobs", blob.GetTableName());
        Assert.Equal("bytea", blob.FindProperty(nameof(ResumeDocumentBlob.Content))!.GetColumnType());
        Assert.Contains(blob.GetCheckConstraints(), x => x.Name == "CK_ResumeDocumentBlobs_FileLength");
        Assert.Contains(blob.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(ResumeDocumentBlob.StorageKey)]));
        var ownerIndex = Assert.Single(blob.GetIndexes(), x => x.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(ResumeDocumentBlob.OwnerUserId), nameof(ResumeDocumentBlob.StorageKey)]));
        Assert.False(ownerIndex.IsUnique);
        var ownerFk = Assert.Single(blob.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(User));
        Assert.Equal(nameof(ResumeDocumentBlob.OwnerUserId), Assert.Single(ownerFk.Properties).Name);
        Assert.Equal(DeleteBehavior.Cascade, ownerFk.DeleteBehavior);
    }

    [Fact]
    public void ProductionResumeStorageIsRegisteredThroughPostgresContextFactory()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=jobportal_test;Username=postgres;SSL Mode=Disable"
        }).Build();
        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<PostgresResumeStorage>(scope.ServiceProvider.GetRequiredService<IResumeStorage>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<JobPortalDbContext>());
    }

    [Theory]
    [InlineData(typeof(Membership))]
    [InlineData(typeof(Payment))]
    [InlineData(typeof(ApplicationQuotaUsage))]
    public void ConcurrencyUsesPostgresXminInsteadOfSqlServerRowVersion(Type entityType)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"{entityType.Name} metadata was not found.");

        Assert.Null(entity.FindProperty("RowVersion"));

        var xmin = entity.FindProperty("xmin");
        Assert.NotNull(xmin);
        Assert.True(xmin!.IsConcurrencyToken);
        Assert.True(xmin.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate);
    }

    [Fact]
    public void PaymentPlanAndAIApplyResumeSnapshotUseBoundedNullableColumns()
    {
        using var context = CreateContext();
        var payment = context.Model.FindEntityType(typeof(Payment))!;
        var application = context.Model.FindEntityType(typeof(AIApplyApplication))!;

        Assert.Equal(50, payment.FindProperty(nameof(Payment.PlanCode))!.GetMaxLength());
        Assert.True(payment.FindProperty(nameof(Payment.PlanCode))!.IsNullable);
        Assert.Equal(255, application.FindProperty(nameof(AIApplyApplication.ResumeStorageKey))!.GetMaxLength());
        Assert.Equal(255, application.FindProperty(nameof(AIApplyApplication.ResumeFileName))!.GetMaxLength());
        Assert.Equal(100, application.FindProperty(nameof(AIApplyApplication.ResumeContentType))!.GetMaxLength());
        Assert.True(application.FindProperty(nameof(AIApplyApplication.ResumeSizeBytes))!.IsNullable);
    }

    private static JobPortalDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=jobportal_test;Username=postgres;SSL Mode=Disable")
            .Options;

        return new JobPortalDbContext(options);
    }
}
