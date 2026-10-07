using System.Security.Cryptography;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.AIResume;
using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIResume;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIResumeDocxPostgresRoundTripTests
{
    [Fact]
    public async Task RealDocxSurvivesDurableStorageRoundTripAndCreatesSessionWithProductionParser()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var factory = new TestContextFactory(options);
        var userId = Guid.NewGuid();
        var resumeId = Guid.NewGuid();
        var role = new Role { Name = "Candidate", NormalizedName = "CANDIDATE" };
        var profile = new CandidateResumeProfile { Id = resumeId, UserId = userId };
        var user = new User
        {
            Id = userId, Role = role, RoleId = role.Id, Email = "morgan@example.invalid",
            NormalizedEmail = "MORGAN@EXAMPLE.INVALID", FirstName = "Morgan", LastName = "Example",
            Status = UserStatus.Active, ResumeProfile = profile
        };
        profile.User = user;
        await using (var seed = factory.CreateDbContext())
        {
            seed.Roles.Add(role);
            seed.Users.Add(user);
            seed.CandidateResumeProfiles.Add(profile);
            await seed.SaveChangesAsync();
        }

        var storage = new PostgresResumeStorage(factory);
        await using var previousResume = ResumeTextExtractorTests.BuildStructuredDocx("Built legacy APIs");
        var previousKey = await storage.StoreAsync(userId, previousResume, ".docx", resumeId,
            "previous-resume.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        var original = ResumeTextExtractorTests.BuildStructuredDocx();
        var bytes = original.ToArray();
        original.Position = 0;
        const string fileName = "Manoj_Shekapure_.Net_Profile.docx";
        const string contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        var storageKey = await storage.StoreAsync(userId, original, ".docx", resumeId, fileName, contentType);
        Assert.NotEqual(previousKey, storageKey);
        await using (var update = factory.CreateDbContext())
        {
            var persistedUser = await update.Users.SingleAsync(x => x.Id == userId);
            persistedUser.ResumeStorageKey = storageKey;
            persistedUser.ResumeFileName = fileName;
            persistedUser.ResumeContentType = contentType;
            persistedUser.ResumeSizeBytes = bytes.LongLength;
            persistedUser.ResumeUploadedAtUtc = DateTime.UtcNow;
            await update.SaveChangesAsync();
        }

        await using (var check = factory.CreateDbContext())
        {
            var blob = await check.ResumeDocumentBlobs.AsNoTracking().SingleAsync(x => x.StorageKey == storageKey);
            Assert.Equal(bytes.LongLength, blob.FileLength);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), blob.Sha256);
            Assert.Equal(bytes, blob.Content);
            Assert.Equal(fileName, blob.OriginalFileName);
            Assert.Equal(contentType, blob.ContentType);
            Assert.Equal(".docx", blob.Extension);
        }

        await using (var read = await storage.OpenReadAsync(userId, storageKey, resumeId, fileName, contentType))
        {
            Assert.NotNull(read);
            Assert.Equal(0, read!.Position);
            using var roundTrip = new MemoryStream();
            await read.CopyToAsync(roundTrip);
            Assert.Equal(bytes.LongLength, roundTrip.Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToHexString(SHA256.HashData(roundTrip.ToArray())));
            Assert.Equal(bytes, roundTrip.ToArray());
        }

        var parser = new StructuredResumeSourceParser(storage, new ResumeTextExtractor());
        await using (var sourceContext = factory.CreateDbContext())
        {
            var candidate = await sourceContext.Users.Include(x => x.ResumeProfile).SingleAsync(x => x.Id == userId);
            var source = await parser.ParseAsync(candidate, default);
            Assert.Equal("Morgan Example", source.Contact.Name);
            Assert.Equal("morgan@example.invalid", source.Contact.Email);
            Assert.Equal(["C#", ".NET"], source.Skills);
            Assert.Equal("Cedar Software", Assert.Single(source.Experience).Employer);
            Assert.Equal(["Built reliable APIs"], source.Experience[0].Bullets);
            Assert.DoesNotContain("Built legacy APIs", source.Experience.SelectMany(x => x.Bullets));
            Assert.NotEmpty(source.Experience);
        }

        await using var serviceContext = factory.CreateDbContext();
        var renderer = new ProfessionalResumeDocumentRenderer();
        var service = new AIResumeService(new AIResumeRepository(serviceContext), new TestProvider(), parser,
            renderer, new TestPhonePeGateway(), Options.Create(new AIResumeOptions()), TimeProvider.System,
            masterDocuments: new OriginalResumeDocuments(storage, renderer));
        var session = await service.CreateSessionAsync(userId,
            new(resumeId, null, "We need an engineer to build reliable backend APIs and maintain production services."), default);

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(resumeId, session.SourceResumeId);
        Assert.Equal(AIResumeSessionStatus.Created, session.Status);
        Assert.Single(await serviceContext.AIResumeSessions.ToListAsync());
    }

    private sealed class TestContextFactory(DbContextOptions<JobPortalDbContext> options) : IDbContextFactory<JobPortalDbContext>
    {
        public JobPortalDbContext CreateDbContext() => new(options);
        public Task<JobPortalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestProvider : IAIResumeProvider
    {
        public Task<AIResumeProviderResult<ResumeAnalysis>> AnalyzeAsync(AnalyzeResumeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AIResumeProviderResult<TailoredResumeContent>> GenerateTailoredResumeAsync(GenerateTailoredResumeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<TailoredResumeValidation> ValidateTailoredResumeAsync(ValidateTailoredResumeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestPhonePeGateway : IPhonePeGateway
    {
        public Task<PhonePeCheckout> CreateCheckoutAsync(string merchantOrderId, long amountInMinorUnits, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PhonePeOrderState> GetOrderStatusAsync(string merchantOrderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public bool VerifyWebhookAuthorization(string authorization) => false;
        public PhonePeCallback ParseCallback(ReadOnlyMemory<byte> rawBody) => throw new NotSupportedException();
    }
}
