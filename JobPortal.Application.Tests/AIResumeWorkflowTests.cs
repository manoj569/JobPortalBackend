using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using JobPortal.Application;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.AIResume;
using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIResume;
using JobPortal.Infrastructure;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIResumeWorkflowTests
{


    [Fact]
    public async Task StructuredParser_UsesTheOwnedUploadedResumeTextAsSourceEvidence()
    {
        var user = new User
        {
            FirstName = "Candidate", LastName = "Test", ResumeStorageKey = "private.pdf", ResumeFileName = "source.pdf",
            ResumeProfile = new CandidateResumeProfile { Id = Guid.NewGuid(), SkillsJson = "[\".NET\"]" }
        };
        var parser = new StructuredResumeSourceParser(new MemoryResumeStorage(), new FakeResumeTextExtractor());

        var parsed = await parser.ParseAsync(user, default);

        Assert.Contains(parsed.Experience[0].Bullets, x => x.Contains("Built reliable data services", StringComparison.Ordinal));
        Assert.Contains(parsed.Skills, x => x == ".NET");
    }

    [Theory]
    [InlineData("Development", 1)]
    [InlineData("Production", 0)]
    public async Task SessionPersistsUploadedFactsThroughDiAndFreshReloadWithSanitizedDiagnostics(string environmentName, int logCount)
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var userId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, Guid.NewGuid(), resumeId);
        await using (var seed = new JobPortalDbContext(options))
        {
            var candidate = await seed.Users.SingleAsync(x => x.Id == userId);
            candidate.ResumeStorageKey = "owned.docx"; candidate.ResumeFileName = "source.docx";
            seed.AIResumeCreditWallets.Add(new AIResumeCreditWallet { UserId = userId, Balance = 1, LifetimePurchased = 1 });
            await seed.SaveChangesAsync();
        }
        var logger = new SourceSnapshotLogger(() =>
        {
            using var beforePersistence = new JobPortalDbContext(options);
            Assert.Empty(beforePersistence.AIResumeSessions);
        });
        var provider = new FakeAIResumeProvider();
        const string apiKey = "fixture-api-secret", workspaceId = "fixture-workspace-secret";
        const string jd = "Backend engineer needed to build reliable C# and .NET APIs and collaborate on data services.";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["AIResume:ApiKey"] = apiKey, ["AIResume:WorkspaceId"] = workspaceId }).Build();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddScoped(_ => new JobPortalDbContext(options));
        services.AddScoped<IAIResumeRepository, AIResumeRepository>();
        services.AddSingleton<IResumeStorage>(new UploadedResumeSourcePipelineTests.DocxStorage());
        services.AddSingleton<IAIResumeProvider>(provider);
        services.AddSingleton<IPhonePeGateway>(new FakePhonePeGateway());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = environmentName });
        services.AddSingleton<ILogger<AIResumeService>>(logger);
        using var container = services.BuildServiceProvider();
        AIResumeSessionResponse response;
        using (var creationScope = container.CreateScope())
        {
            Assert.IsType<StructuredResumeSourceParser>(creationScope.ServiceProvider.GetRequiredService<IAIResumeSourceParser>());
            Assert.IsType<ResumeTextExtractor>(creationScope.ServiceProvider.GetRequiredService<IResumeTextExtractor>());
            response = await creationScope.ServiceProvider.GetRequiredService<IAIResumeService>()
                .CreateSessionAsync(userId, new(resumeId, null, jd), default);
        }
        // A fresh context reads only persisted JSON, independent of the creation scope's change tracker.
        await using var reload = new JobPortalDbContext(options);
        var session = await reload.AIResumeSessions.AsNoTracking().SingleAsync(x => x.Id == response.Id);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var source = JsonSerializer.Deserialize<TailoredResumeContent>(session.SourceJson, json)!;
        var evidence = JsonSerializer.Deserialize<ResumeSessionEvidence>(session.EvidenceJson, json)!.Evidence;
        Assert.Equal("Morgan Example", source.Contact.Name);
        Assert.Equal(20, source.Skills.Length);
        Assert.Equal(2, source.Experience.Length);
        Assert.Equal(2, source.Projects.Length);
        Assert.Equal(2, source.Education.Length);
        Assert.Single(source.Certifications);
        Assert.Empty(source.AdditionalInfo);
        Assert.Equal(39, evidence.Length);
        Assert.Equal(ResumeEvidenceCatalog.Create(source), evidence);
        Assert.Contains(evidence, x => x.Scope == "skills" && x.Text == "C#");
        Assert.Contains(evidence, x => x.Scope == "experienceIdentity/0" && x.Text.Contains("Cedar Software", StringComparison.Ordinal));
        Assert.Contains(evidence, x => x.Scope == "projectIdentity/0" && x.Text == "Inventory Portal");
        Assert.Contains(evidence, x => x.Scope == "education" && x.Text.Contains("Example Institute", StringComparison.Ordinal));
        Assert.Equal(logCount, logger.Messages.Count);
        foreach (var message in logger.Messages)
        {
            const string prefix = "AIResumeSourceSnapshot ";
            Assert.StartsWith(prefix, message);
            using var metadata = JsonDocument.Parse(message[prefix.Length..]);
            Assert.Equal(new[] { "SessionId", "SkillsCount", "ExperienceCount", "ProjectsCount", "EducationCount",
                "CertificationsCount", "AdditionalInfoCount", "EvidenceCount", "HasContact", "HasSummary", "SourceResumeId" },
                metadata.RootElement.EnumerateObject().Select(x => x.Name));
            Assert.Equal(response.Id, metadata.RootElement.GetProperty("SessionId").GetGuid());
            Assert.Equal(resumeId, metadata.RootElement.GetProperty("SourceResumeId").GetGuid());
            Assert.Equal(source.Skills.Length, metadata.RootElement.GetProperty("SkillsCount").GetInt32());
            Assert.Equal(source.Experience.Length, metadata.RootElement.GetProperty("ExperienceCount").GetInt32());
            Assert.Equal(source.Projects.Length, metadata.RootElement.GetProperty("ProjectsCount").GetInt32());
            Assert.Equal(source.Education.Length, metadata.RootElement.GetProperty("EducationCount").GetInt32());
            Assert.Equal(source.Certifications.Length, metadata.RootElement.GetProperty("CertificationsCount").GetInt32());
            Assert.Equal(source.AdditionalInfo.Length, metadata.RootElement.GetProperty("AdditionalInfoCount").GetInt32());
            Assert.Equal(evidence.Length, metadata.RootElement.GetProperty("EvidenceCount").GetInt32());
            Assert.True(metadata.RootElement.GetProperty("HasContact").GetBoolean());
            Assert.True(metadata.RootElement.GetProperty("HasSummary").GetBoolean());
            foreach (var sensitive in new[] { source.Contact.Name, source.Contact.Email, source.Contact.Phone,
                source.Contact.Location, source.ProfessionalSummary, source.Experience[0].Employer, source.Projects[0].Name,
                source.Education[0].Institution, UploadedResumeSourcePipelineTests.Resume, jd, apiKey, workspaceId })
                Assert.DoesNotContain(sensitive, message);
        }
        // Only the fake provider is called: prove generation receives the identical stored source.
        using var generationScope = container.CreateScope();
        var service = generationScope.ServiceProvider.GetRequiredService<IAIResumeService>();
        await service.AnalyzeAsync(userId, response.Id, default);
        await service.GenerateAsync(userId, response.Id, new(Guid.NewGuid()), default);
        Assert.Equal(session.SourceJson, provider.GenerationSourceJson);
        Assert.Equal(logCount, logger.Messages.Count);
    }

    [Fact]
    public void EvidenceGuard_AllowsGroundedParaphrase_ButRejectsFabricatedMetricAndTeamSize()
    {
        var source = Source();
        var evidence = ResumeEvidenceCatalog.Create(source);
        var validText = "Developed and maintained REST APIs using .NET and SQL Server.";
        var valid = source with { Experience = [source.Experience[0] with { Bullets = [validText] }], Evidence = [
            new("experience/0/bullets/0", validText, [evidence.Single(x => x.Scope == "experience/0").Id])
        ] };
        Assert.True(AIResumeContentGuard.Validate(source, valid).IsValid);

        var metric = "Improved API performance by 40%.";
        var fabricatedMetric = source with { Experience = [source.Experience[0] with { Bullets = [metric] }], Evidence = [
            new("experience/0/bullets/0", metric, [evidence.Single(x => x.Scope == "experience/0").Id])
        ] };
        Assert.False(AIResumeContentGuard.Validate(source, fabricatedMetric).IsValid);

        var leadership = "Led a team of 8 React developers.";
        var fabricatedLeadership = source with { Experience = [source.Experience[0] with { Bullets = [leadership] }], Evidence = [
            new("experience/0/bullets/0", leadership, [evidence.Single(x => x.Scope == "experience/0").Id])
        ] };
        Assert.False(AIResumeContentGuard.Validate(source, fabricatedLeadership).IsValid);

        var technology = "Built Kubernetes services.";
        var fabricatedTechnology = source with { Experience = [source.Experience[0] with { Bullets = [technology] }], Evidence = [
            new("experience/0/bullets/0", technology, [evidence.Single(x => x.Scope == "experience/0").Id])
        ] };
        Assert.False(AIResumeContentGuard.Validate(source, fabricatedTechnology).IsValid);

        var mixedFactsSource = source with { Experience = [source.Experience[0] with { Bullets = [
            "Improved API performance.", "Reduced hosting costs by 40%."
        ] }] };
        var mixedMetric = "Improved API performance by 40%.";
        var mixedFactsRewrite = mixedFactsSource with
        {
            Experience = [mixedFactsSource.Experience[0] with { Bullets = [mixedMetric] }],
            Evidence = [new("experience/0/bullets/0", mixedMetric, [
                "SRC-EXP-001-BULLET-001", "SRC-EXP-001-BULLET-002"
            ])]
        };
        Assert.False(AIResumeContentGuard.Validate(mixedFactsSource, mixedFactsRewrite).IsValid);

        var quantifiedSource = source with { Experience = [source.Experience[0] with { Bullets = ["Reduced processing time by 40%."] }] };
        var supportedQuantifiedClaim = "Improved processing performance by 40%.";
        var quantifiedRewrite = quantifiedSource with
        {
            Experience = [quantifiedSource.Experience[0] with { Bullets = [supportedQuantifiedClaim] }],
            Evidence = [new("experience/0/bullets/0", supportedQuantifiedClaim, ["SRC-EXP-001-BULLET-001"])]
        };
        Assert.True(AIResumeContentGuard.Validate(quantifiedSource, quantifiedRewrite).IsValid);
    }

    [Fact]
    public async Task ExternalJd_AnalysisPurchaseGenerateEditAndRedownload_UsesCreditsExactlyOnce()
    {
        var database = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(database).Options;
        var userId = Guid.NewGuid(); var roleId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        var role = new Role { Id = roleId, Name = "Candidate", NormalizedName = "CANDIDATE" };
        var user = new User
        {
            Id = userId, RoleId = roleId, Role = role, Email = "candidate@example.test", NormalizedEmail = "CANDIDATE@EXAMPLE.TEST",
            FirstName = "Candidate", LastName = "Test", Status = UserStatus.Active,
            ResumeStorageKey = "private.pdf", ResumeFileName = "source.pdf", ResumeProfile = new CandidateResumeProfile { Id = resumeId, UserId = userId }
        };
        user.ResumeProfile.User = user;
        await using (var seed = new JobPortalDbContext(options))
        {
            seed.Roles.Add(role); seed.Users.Add(user); seed.CandidateResumeProfiles.Add(user.ResumeProfile);
            await seed.SaveChangesAsync();
        }

        var gateway = new FakePhonePeGateway();
        var provider = new FakeAIResumeProvider();
        var renderer = new ProfessionalResumeDocumentRenderer();
        AIResumeService CreateService(JobPortalDbContext context) => new(new AIResumeRepository(context), provider,
            new FakeSourceParser(), renderer, gateway, Options.Create(new AIResumeOptions()), TimeProvider.System);
        await using var db = new JobPortalDbContext(options);
        var service = CreateService(db);

        var session = await service.CreateSessionAsync(userId,
            new(resumeId, null, "We are seeking an engineer to build backend APIs, collaborate with product teams, and improve reliable data services."), default);
        var analyzed = await service.AnalyzeAsync(userId, session.Id, default);
        Assert.Equal(87, analyzed.Analysis!.OverallMatchScore);
        Assert.Equal("Candidate", provider.AnalysisContactName);
        Assert.Equal(0, (await service.CreditsAsync(userId, default)).Balance);
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.ConflictException>(() =>
            service.GenerateAsync(userId, session.Id, new(Guid.NewGuid()), default));

        var firstPurchaseKey = Guid.NewGuid();
        var checkout = await service.CheckoutAsync(userId, new("AI_RESUME_1", firstPurchaseKey, session.Id), default);
        Assert.Equal(1900, gateway.LastAmountInMinorUnits);
        Assert.Equal(0, (await service.CreditsAsync(userId, default)).Balance);
        gateway.State = PhonePeOrderStateKind.Completed;
        var paid = await service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default);
        Assert.Equal(PaymentStatus.Paid, paid.Status);
        await service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default);
        Assert.Equal(1, (await service.CreditsAsync(userId, default)).Balance);

        var requestKey = Guid.NewGuid();
        var generated = await service.GenerateAsync(userId, session.Id, new(requestKey), default);
        Assert.Equal(0, (await service.CreditsAsync(userId, default)).Balance);
        var retry = await service.GenerateAsync(userId, session.Id, new(requestKey), default);
        Assert.Equal(generated.Id, retry.Id);
        Assert.Equal(1, provider.GenerationCalls);

        var edited = await service.EditAsync(userId, generated.Id, new(0, generated.Content), default);
        Assert.Equal(1, edited.EditRevision);
        var pdf = await service.DownloadAsync(userId, generated.Id, "pdf", default);
        var docx = await service.DownloadAsync(userId, generated.Id, "docx", default);
        var text = await service.DownloadAsync(userId, generated.Id, "txt", default);
        var pdfAgain = await service.DownloadAsync(userId, generated.Id, "pdf", default);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(pdf.Content));
        Assert.Equal("PK", Encoding.ASCII.GetString(docx.Content, 0, 2));
        Assert.Contains("Candidate Test", Encoding.UTF8.GetString(text.Content));
        Assert.Equal(pdf.Content, pdfAgain.Content);
        Assert.Equal(0, (await service.CreditsAsync(userId, default)).Balance);

        var second = await service.CheckoutAsync(userId, new("AI_RESUME_1", Guid.NewGuid()), default);
        await service.PhonePeReturnAsync(userId, second.MerchantOrderId, default);
        Assert.Equal(1, (await service.CreditsAsync(userId, default)).Balance);
        var regenerated = await service.GenerateAsync(userId, session.Id, new(Guid.NewGuid()), default);
        Assert.NotEqual(generated.Id, regenerated.Id);
        var wallet = await service.CreditsAsync(userId, default);
        Assert.Equal(0, wallet.Balance); Assert.Equal(2, wallet.LifetimePurchased); Assert.Equal(2, wallet.LifetimeConsumed);
        var history = await service.HistoryAsync(userId, 1, 20, default);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task UploadedResumeCompleteWorkflowUsesGroundedContentAndExactlyOnceCreditsThroughEditsDownloadsRegenerationAndFailure()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var userId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, Guid.NewGuid(), resumeId);
        await using var db = new JobPortalDbContext(options);
        var candidate = await db.Users.SingleAsync(x => x.Id == userId);
        candidate.ResumeStorageKey = "owned.docx"; candidate.ResumeFileName = "resume.docx";
        await db.SaveChangesAsync();
        var provider = new FakeAIResumeProvider(); var payment = new FakePhonePeGateway();
        var service = new AIResumeService(new AIResumeRepository(db), provider,
            new StructuredResumeSourceParser(new UploadedResumeSourcePipelineTests.DocxStorage(), new ResumeTextExtractor()),
            new ProfessionalResumeDocumentRenderer(), payment, Options.Create(new AIResumeOptions()), TimeProvider.System);
        var session = await service.CreateSessionAsync(userId, new(resumeId, null,
            "Backend developer required to build reliable C# and .NET services and work with SQL Server."), default);
        var snapshot = (await db.AIResumeSessions.SingleAsync()).SourceJson;
        var source = JsonSerializer.Deserialize<TailoredResumeContent>(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(20, source.Skills.Length); Assert.Equal(2, source.Experience.Length);
        Assert.Equal(39, ResumeEvidenceCatalog.Create(source).Length);
        Assert.Equal(0, (await service.CreditsAsync(userId, default)).LifetimeConsumed);
        await service.AnalyzeAsync(userId, session.Id, default);
        Assert.Equal(0, (await service.CreditsAsync(userId, default)).LifetimeConsumed);
        var checkout = await service.CheckoutAsync(userId, new("AI_RESUME_5", Guid.NewGuid(), session.Id), default);
        Assert.Equal(7900, payment.LastAmountInMinorUnits);
        payment.State = PhonePeOrderStateKind.Completed;
        await service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default);
        await service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default);
        Assert.Equal(5, (await service.CreditsAsync(userId, default)).Balance);
        provider.GenerationOverride = ClaudeAIResumeProviderTests.GroundedFixture(source);
        provider.OnGeneration = () => { Assert.Equal(1, db.AIResumeCreditWallets.Single().Reserved); Assert.Equal(4, db.AIResumeCreditWallets.Single().Balance); };
        var generationKey = Guid.NewGuid();
        var generated = await service.GenerateAsync(userId, session.Id, new(generationKey), default);
        Assert.Equal(generated.Id, (await service.GenerateAsync(userId, session.Id, new(generationKey), default)).Id);
        provider.OnGeneration = null;
        Assert.Equal(1, provider.GenerationCalls);
        Assert.True(AIResumeContentGuard.Validate(source, generated.Content).IsValid);
        Assert.Equal(generated.Id, (await service.GetResumeAsync(userId, generated.Id, default)).Id);
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.BadRequestException>(() => service.EditAsync(userId, generated.Id,
            new(0, generated.Content with { ProfessionalSummary = "Edited by the candidate." }), default));
        var editedContent = generated.Content with { ProfessionalSummary = source.ProfessionalSummary,
            Evidence = generated.Content.Evidence.Where(x => x.Path != "summary").ToArray() };
        var edited = await service.EditAsync(userId, generated.Id, new(0, editedContent), default);
        Assert.Equal(editedContent.ProfessionalSummary, (await service.GetResumeAsync(userId, generated.Id, default)).Content.ProfessionalSummary);
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.ConflictException>(() => service.EditAsync(userId, generated.Id, new(0, editedContent), default));
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.NotFoundException>(() => service.GetResumeAsync(Guid.NewGuid(), generated.Id, default));
        foreach (var format in new[] { "pdf", "docx", "txt" })
        {
            var document = await service.DownloadAsync(userId, generated.Id, format, default);
            Assert.NotEmpty(document.Content); Assert.Equal($"Morgan_Example_Resume.{format}", document.FileName);
            if (format == "txt") Assert.Contains(editedContent.ProfessionalSummary, Encoding.UTF8.GetString(document.Content));
        }
        Assert.Equal(1, provider.GenerationCalls);
        Assert.Equal(1, (await service.CreditsAsync(userId, default)).LifetimeConsumed);
        var second = await service.GenerateAsync(userId, session.Id, new(Guid.NewGuid()), default);
        Assert.Equal(2, second.Version);
        provider.GenerationOverride = source with { Experience = [source.Experience[0] with { Employer = "Invented employer" }] };
        var error = await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.AppException>(() => service.GenerateAsync(userId, session.Id, new(Guid.NewGuid()), default));
        Assert.Equal("unsupported_claims", error.Code);
        await service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default);
        var wallet = await service.CreditsAsync(userId, default);
        Assert.Equal(3, wallet.Balance); Assert.Equal(0, wallet.Reserved); Assert.Equal(2, wallet.LifetimeConsumed); Assert.Equal(5, wallet.LifetimePurchased);
        Assert.Equal(2, await db.TailoredResumes.CountAsync());
        Assert.Equal(AIResumeSessionStatus.Generated, (await service.GetSessionAsync(userId, session.Id, default)).Status);
        Assert.Equal(snapshot, (await db.AIResumeSessions.SingleAsync()).SourceJson);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("validation")]
    [InlineData("skill")]
    [InlineData("timeout")]
    [InlineData("cancellation")]
    [InlineData("unexpected")]
    public async Task AnalysisFailureReleasesLeaseAndIsFreeAndRetryable(string failure)
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var userId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, Guid.NewGuid(), resumeId);
        await using var db = new JobPortalDbContext(options);
        var provider = new FakeAIResumeProvider { AnalysisFailure = TestFailure(failure) };
        if (failure == "validation") { provider.AnalysisFailure = null; provider.AnalysisOverride = ClaudeAIResumeProviderTests.Analysis with { OverallMatchScore = 200 }; }
        if (failure == "skill") { provider.AnalysisFailure = null; provider.AnalysisOverride = ClaudeAIResumeProviderTests.Analysis with { MatchedSkills = ["C#"] }; }
        var service = new AIResumeService(new AIResumeRepository(db), provider, new FakeSourceParser(), new ProfessionalResumeDocumentRenderer(),
            new FakePhonePeGateway(), Options.Create(new AIResumeOptions()), TimeProvider.System);
        var session = await service.CreateSessionAsync(userId, new(resumeId, null, new string('x', 60)), default);
        await Assert.ThrowsAnyAsync<Exception>(() => service.AnalyzeAsync(userId, session.Id, default));
        var row = await db.AIResumeSessions.SingleAsync();
        Assert.Equal(AIResumeSessionStatus.Created, row.Status); Assert.Null(row.AnalysisOwner); Assert.Null(row.AnalysisLeaseUntilUtc); Assert.Null(row.AnalysisJson);
        Assert.Empty(db.AIResumeCreditTransactions); Assert.Empty(db.AIResumeCreditWallets);
        provider.AnalysisFailure = null; provider.AnalysisOverride = null;
        Assert.Equal(AIResumeSessionStatus.Analyzed, (await service.AnalyzeAsync(userId, session.Id, default)).Status);
        Assert.Empty(db.AIResumeCreditTransactions);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("json")]
    [InlineData("validation")]
    [InlineData("timeout")]
    [InlineData("cancellation")]
    [InlineData("persistence")]
    [InlineData("unexpected")]
    public async Task GenerationFailureRestoresOnceAndSameIdempotencyKeyCanRetry(string failure)
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var userId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, Guid.NewGuid(), resumeId);
        await using var db = new JobPortalDbContext(options);
        db.AIResumeCreditWallets.Add(new AIResumeCreditWallet { UserId = userId, Balance = 1, LifetimePurchased = 1 });
        await db.SaveChangesAsync();
        var proxy = DispatchProxy.Create<IAIResumeRepository, FailingWriteRepository>();
        var fault = (FailingWriteRepository)(object)proxy; fault.Inner = new AIResumeRepository(db);
        var provider = new FakeAIResumeProvider();
        var service = new AIResumeService(proxy, provider, new FakeSourceParser(), new ProfessionalResumeDocumentRenderer(),
            new FakePhonePeGateway(), Options.Create(new AIResumeOptions()), TimeProvider.System);
        var session = await service.CreateSessionAsync(userId, new(resumeId, null, new string('x', 60)), default);
        await service.AnalyzeAsync(userId, session.Id, default);
        if (failure == "persistence") fault.FailAtWrite = fault.Writes + 2;
        else if (failure == "validation") provider.GenerationOverride = Source() with { Skills = ["Invented skill"] };
        else provider.GenerationFailure = TestFailure(failure);
        provider.OnGeneration = () => Assert.Equal(1, db.AIResumeCreditWallets.Single().Reserved);
        var key = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<Exception>(() => service.GenerateAsync(userId, session.Id, new(key), default));
        var wallet = await service.CreditsAsync(userId, default);
        Assert.Equal(1, wallet.Balance); Assert.Equal(0, wallet.Reserved); Assert.Equal(0, wallet.LifetimeConsumed);
        Assert.Empty(db.TailoredResumes);
        Assert.Equal(1, await db.AIResumeCreditTransactions.CountAsync(x => x.Kind == AIResumeCreditKind.Release));
        Assert.Equal(AIResumeSessionStatus.Analyzed, (await service.GetSessionAsync(userId, session.Id, default)).Status);
        provider.GenerationFailure = null; provider.GenerationOverride = null;
        var result = await service.GenerateAsync(userId, session.Id, new(key), default);
        Assert.Equal(result.Id, (await service.GenerateAsync(userId, session.Id, new(key), default)).Id);
        wallet = await service.CreditsAsync(userId, default);
        Assert.Equal(0, wallet.Balance); Assert.Equal(0, wallet.Reserved); Assert.Equal(1, wallet.LifetimeConsumed);
        Assert.Equal(1, await db.AIResumeCreditTransactions.CountAsync(x => x.Kind == AIResumeCreditKind.Release));
    }

    private static Exception TestFailure(string failure) => failure switch
    {
        "timeout" => new AIResumeProviderException("provider_timeout", true),
        "json" => new AIResumeProviderException("provider_invalid_response"),
        "cancellation" => new OperationCanceledException(),
        "unexpected" => new InvalidOperationException("Test-only unexpected failure"),
        _ => new AIResumeProviderException("invalid_analysis")
    };

    [Fact]
    public async Task RefreshRecoversExpiredAnalysisAndGenerationLeasesWithoutDoubleRefund()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var userId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, Guid.NewGuid(), resumeId);
        await using var db = new JobPortalDbContext(options);
        var provider = new FakeAIResumeProvider();
        var service = new AIResumeService(new AIResumeRepository(db), provider, new FakeSourceParser(), new ProfessionalResumeDocumentRenderer(),
            new FakePhonePeGateway(), Options.Create(new AIResumeOptions()), TimeProvider.System);
        var session = await service.CreateSessionAsync(userId, new(resumeId, null, new string('x', 60)), default);
        var row = await db.AIResumeSessions.SingleAsync();
        row.Status = AIResumeSessionStatus.Analyzing; row.AnalysisOwner = Guid.NewGuid(); row.AnalysisLeaseUntilUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        Assert.Equal(AIResumeSessionStatus.Created, (await service.GetSessionAsync(userId, session.Id, default)).Status);
        Assert.Null(row.AnalysisOwner); Assert.Empty(db.AIResumeCreditTransactions);
        await service.AnalyzeAsync(userId, session.Id, default);
        db.AIResumeCreditWallets.Add(new AIResumeCreditWallet { UserId = userId, Balance = 0, Reserved = 1, LifetimePurchased = 1 });
        var key = Guid.NewGuid();
        db.AIResumeGenerations.Add(new AIResumeGeneration { UserId = userId, SessionId = session.Id, RequestKey = key,
            Owner = Guid.NewGuid(), LeaseUntilUtc = DateTime.UtcNow.AddMinutes(-1), Status = AIResumeGenerationStatus.Reserved, Attempt = 1 });
        row.Status = AIResumeSessionStatus.Generating;
        await db.SaveChangesAsync();
        Assert.Equal(AIResumeSessionStatus.Analyzed, (await service.GetSessionAsync(userId, session.Id, default)).Status);
        await service.GetSessionAsync(userId, session.Id, default);
        Assert.Equal(1, (await service.CreditsAsync(userId, default)).Balance);
        Assert.Equal(1, await db.AIResumeCreditTransactions.CountAsync(x => x.Kind == AIResumeCreditKind.Release));
        await service.GenerateAsync(userId, session.Id, new(key), default);
        Assert.Equal(1, (await service.CreditsAsync(userId, default)).LifetimeConsumed);
    }

    [Theory]
    [InlineData(PhonePeOrderStateKind.Pending)]
    [InlineData(PhonePeOrderStateKind.Failed)]
    [InlineData(PhonePeOrderStateKind.Cancelled)]
    public async Task UncompletedOrMismatchedPaymentsAndNonOwnerAccessNeverGrantCredits(PhonePeOrderStateKind state)
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var userId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, Guid.NewGuid(), resumeId);
        await using var db = new JobPortalDbContext(options);
        var gateway = new FakePhonePeGateway();
        var service = new AIResumeService(new AIResumeRepository(db), new FakeAIResumeProvider(), new FakeSourceParser(),
            new ProfessionalResumeDocumentRenderer(), gateway, Options.Create(new AIResumeOptions()), TimeProvider.System);
        var checkout = await service.CheckoutAsync(userId, new("AI_RESUME_25", Guid.NewGuid()), default);
        Assert.Equal(24900, gateway.LastAmountInMinorUnits);
        gateway.State = state;
        await service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default);
        Assert.Equal(0, (await service.CreditsAsync(userId, default)).Balance);
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.NotFoundException>(() => service.PhonePeReturnAsync(Guid.NewGuid(), checkout.MerchantOrderId, default));
        gateway.State = PhonePeOrderStateKind.Completed; gateway.AmountOverride = 1;
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.ConflictException>(() => service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default));
        Assert.Empty(db.AIResumeCreditTransactions);
        gateway.AmountOverride = null;
        await service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default);
        await service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default);
        Assert.Equal(25, (await service.CreditsAsync(userId, default)).Balance);
        Assert.Single(db.AIResumeCreditTransactions);
    }

    public class FailingWriteRepository : DispatchProxy
    {
        public IAIResumeRepository Inner { get; set; } = null!;
        public int Writes { get; private set; }
        public int FailAtWrite { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IAIResumeRepository.WriteAsync) && ++Writes == FailAtWrite)
                throw new DbUpdateException("Test-only persistence failure");
            try { return method.Invoke(Inner, args); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
    }

    [Fact]
    public async Task PhonePeWebhookAndReturnReplay_GrantPurchaseCreditsOnce()
    {
        var database = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(database).Options;
        var userId = Guid.NewGuid(); var roleId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, roleId, resumeId);
        var gateway = new FakePhonePeGateway { State = PhonePeOrderStateKind.Completed };
        var provider = new FakeAIResumeProvider();
        AIResumeService CreateService(JobPortalDbContext context) => new(new AIResumeRepository(context), provider,
            new FakeSourceParser(), new ProfessionalResumeDocumentRenderer(), gateway, Options.Create(new AIResumeOptions()), TimeProvider.System);
        await using var db = new JobPortalDbContext(options);
        var service = CreateService(db);
        var checkout = await service.CheckoutAsync(userId, new("AI_RESUME_5", Guid.NewGuid()), default);

        await using var db2 = new JobPortalDbContext(options);
        var otherService = CreateService(db2);
        var webhook = new PhonePeWebhookRequest(Encoding.UTF8.GetBytes("verified-fixture"), "signed-fixture");
        await Task.WhenAll(service.PhonePeReturnAsync(userId, checkout.MerchantOrderId, default),
            otherService.ProcessPhonePeWebhookAsync(webhook, default));
        Assert.Equal(5, (await service.CreditsAsync(userId, default)).Balance);
        await otherService.ProcessPhonePeWebhookAsync(webhook, default);
        Assert.Equal(5, (await service.CreditsAsync(userId, default)).Balance);
        Assert.Equal(1, await db.AIResumeCreditTransactions.CountAsync(x => x.PurchaseId != null && x.Kind == AIResumeCreditKind.Purchase));
    }

    [Fact]
    public async Task CreditLedger_RejectsMutationAndDeletion()
    {
        var database = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(database).Options;
        var userId = Guid.NewGuid(); var roleId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, roleId, resumeId);
        await using var context = new JobPortalDbContext(options);
        var entry = new AIResumeCreditTransaction { UserId = userId, AvailableDelta = 1, BalanceAfter = 1, IdempotencyKey = "test-ledger-entry" };
        context.AIResumeCreditTransactions.Add(entry);
        await context.SaveChangesAsync();

        entry.AvailableDelta = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.Entry(entry).State = EntityState.Unchanged;
        context.AIResumeCreditTransactions.Remove(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task CareerHarborJobSession_UsesPublishedJobSnapshot()
    {
        var database = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(database).Options;
        var userId = Guid.NewGuid(); var roleId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, roleId, resumeId);
        var categoryId = Guid.NewGuid(); var companyId = Guid.NewGuid(); var jobId = Guid.NewGuid();
        await using (var seed = new JobPortalDbContext(options))
        {
            var candidate = await seed.Users.SingleAsync(x => x.Id == userId);
            var company = new Company { Id = companyId, Name = "Authoritative Company", NormalizedName = "AUTHORITATIVE COMPANY", Slug = "authoritative-company", OwnerUserId = userId, OwnerUser = candidate };
            var category = new Category { Id = categoryId, Name = "Engineering", Slug = "engineering" };
            seed.Companies.Add(company); seed.Categories.Add(category);
            seed.Jobs.Add(new Job
            {
                Id = jobId, ReferenceNumber = "AI-R-1", Title = "Backend Engineer", Slug = "backend-engineer", Description = new string('x', 60),
                ApplicationUrl = "https://jobs.example.test/backend", Status = JobStatus.Published, IsHidden = false,
                PublishedAtUtc = DateTime.UtcNow.AddMinutes(-5), ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
                CompanyId = companyId, Company = company, CategoryId = categoryId, Category = category
            });
            await seed.SaveChangesAsync();
        }
        var gateway = new FakePhonePeGateway(); var provider = new FakeAIResumeProvider();
        await using var context = new JobPortalDbContext(options);
        var service = new AIResumeService(new AIResumeRepository(context), provider, new FakeSourceParser(),
            new ProfessionalResumeDocumentRenderer(), gateway, Options.Create(new AIResumeOptions()), TimeProvider.System);

        var session = await service.CreateSessionAsync(userId, new(resumeId, jobId, null), default);

        Assert.Equal("CAREERHARBOR_JOB", session.SourceType);
        Assert.Equal("Backend Engineer", session.JobTitle);
        Assert.Equal("Authoritative Company", session.CompanyName);
    }

    [Fact]
    public async Task SessionCreationAcceptsOwnedCandidateResumeIdAndRejectsAnotherCandidatesResumeId()
    {
        var database = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(database).Options;
        var candidateId = Guid.NewGuid(); var roleId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        var otherCandidateId = Guid.NewGuid(); var otherResumeId = Guid.NewGuid();
        await SeedCandidate(options, candidateId, roleId, resumeId);
        await using (var seed = new JobPortalDbContext(options))
        {
            var role = await seed.Roles.SingleAsync(x => x.Id == roleId);
            var other = new User
            {
                Id = otherCandidateId, RoleId = roleId, Role = role,
                Email = "other-candidate@example.test", NormalizedEmail = "OTHER-CANDIDATE@EXAMPLE.TEST",
                FirstName = "Other", LastName = "Candidate", Status = UserStatus.Active,
                ResumeStorageKey = "other-private.pdf", ResumeFileName = "other.pdf",
                ResumeProfile = new CandidateResumeProfile { Id = otherResumeId, UserId = otherCandidateId }
            };
            other.ResumeProfile.User = other;
            seed.Users.Add(other);
            seed.CandidateResumeProfiles.Add(other.ResumeProfile);
            await seed.SaveChangesAsync();
        }

        await using var context = new JobPortalDbContext(options);
        var service = new AIResumeService(new AIResumeRepository(context), new FakeAIResumeProvider(),
            new FakeSourceParser(), new ProfessionalResumeDocumentRenderer(), new FakePhonePeGateway(),
            Options.Create(new AIResumeOptions()), TimeProvider.System);
        const string description = "We are seeking an engineer to build backend APIs, collaborate with product teams, and improve reliable data services.";

        var session = await service.CreateSessionAsync(candidateId,
            new(resumeId, null, description), default);

        Assert.Equal(resumeId, session.SourceResumeId);
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.NotFoundException>(() =>
            service.CreateSessionAsync(candidateId, new(otherResumeId, null, description), default));
    }

    [Fact]
    public async Task ConcurrentGenerate_OnlyOneReservationAndConsumption()
    {
        var database = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(database).Options;
        var userId = Guid.NewGuid(); var roleId = Guid.NewGuid(); var resumeId = Guid.NewGuid();
        await SeedCandidate(options, userId, roleId, resumeId);
        var provider = new FakeAIResumeProvider { BlockGeneration = true };
        var gateway = new FakePhonePeGateway();
        AIResumeService CreateService(JobPortalDbContext context) => new(new AIResumeRepository(context), provider,
            new FakeSourceParser(), new ProfessionalResumeDocumentRenderer(), gateway, Options.Create(new AIResumeOptions()), TimeProvider.System);
        await using var firstContext = new JobPortalDbContext(options);
        var firstService = CreateService(firstContext);
        var session = await firstService.CreateSessionAsync(userId,
            new(resumeId, null, "We are seeking an engineer to build backend APIs, collaborate with product teams, and improve reliable data services."), default);
        await firstService.AnalyzeAsync(userId, session.Id, default);
        firstContext.AIResumeCreditWallets.Add(new AIResumeCreditWallet { UserId = userId, Balance = 1, LifetimePurchased = 1 });
        await firstContext.SaveChangesAsync();
        await using var secondContext = new JobPortalDbContext(options);
        var secondService = CreateService(secondContext);
        var key = Guid.NewGuid();

        var first = firstService.GenerateAsync(userId, session.Id, new(key), default);
        await provider.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.ConflictException>(() =>
            secondService.GenerateAsync(userId, session.Id, new(key), default));
        provider.ContinueGeneration.TrySetResult();
        var generated = await first;

        Assert.Equal(1, provider.GenerationCalls);
        Assert.Equal(0, (await secondService.CreditsAsync(userId, default)).Balance);
        Assert.Equal(1, (await firstService.CreditsAsync(userId, default)).LifetimeConsumed);
        Assert.NotEqual(Guid.Empty, generated.Id);
    }

    private static async Task SeedCandidate(DbContextOptions<JobPortalDbContext> options, Guid userId, Guid roleId, Guid resumeId)
    {
        await using var seed = new JobPortalDbContext(options);
        var role = new Role { Id = roleId, Name = "Candidate", NormalizedName = "CANDIDATE" };
        var candidate = new User
        {
            Id = userId, RoleId = roleId, Role = role, Email = "candidate@example.test", NormalizedEmail = "CANDIDATE@EXAMPLE.TEST",
            FirstName = "Candidate", LastName = "Test", Status = UserStatus.Active,
            ResumeStorageKey = "private.pdf", ResumeFileName = "source.pdf", ResumeProfile = new CandidateResumeProfile { Id = resumeId, UserId = userId }
        };
        candidate.ResumeProfile.User = candidate;
        seed.Roles.Add(role); seed.Users.Add(candidate); seed.CandidateResumeProfiles.Add(candidate.ResumeProfile);
        await seed.SaveChangesAsync();
    }

    private static TailoredResumeContent Source() => new(new("Candidate Test", "candidate@example.test", "", "", []),
        "Software developer", ["React", ".NET", "SQL Server"],
        [new("Example Co", "Developer", "2020-01", "2024-01", ["Worked on APIs using .NET and SQL Server."])], [], [], [], []);

    private sealed class FakeSourceParser : IAIResumeSourceParser
    {
        public Task<TailoredResumeContent> ParseAsync(User candidate, CancellationToken ct) => Task.FromResult(Source());
    }

    private sealed class MemoryResumeStorage : IResumeStorage
    {
        public Task<string> StoreAsync(Stream content, string extension, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("test fixture")));
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeResumeTextExtractor : IResumeTextExtractor
    {
        public Task<string> ExtractAsync(Stream content, string extension, CancellationToken cancellationToken = default) =>
            Task.FromResult(UploadedResumeSourcePipelineTests.Resume);
    }

    private sealed class FakeAIResumeProvider : IAIResumeProvider
    {
        public int GenerationCalls { get; private set; }
        public string? GenerationSourceJson { get; private set; }
        public string? AnalysisContactName { get; private set; }
        public bool BlockGeneration { get; init; }
        public Exception? AnalysisFailure { get; set; }
        public ResumeAnalysis? AnalysisOverride { get; set; }
        public Exception? GenerationFailure { get; set; }
        public TailoredResumeContent? GenerationOverride { get; set; }
        public Action? OnGeneration { get; set; }
        public Task<AIResumeProviderResult<TailoringPatch>> GenerateTailoringPatchAsync(GenerateTailoringPatchRequest request, CancellationToken cancellationToken = default)
        {
            GenerationCalls++;
            GenerationSourceJson = JsonSerializer.Serialize(request.Source, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var target = request.EditableTargets.First(x => x.Scope.StartsWith("experience/", StringComparison.Ordinal));
            return Task.FromResult(new AIResumeProviderResult<TailoringPatch>(new([new(target.Id, target.Text,
                target.Text.Replace("Built", "Developed", StringComparison.Ordinal), [target.Id], [], "Supported wording")], []), "fake", 20, 10));
        }
        public TaskCompletionSource GenerationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueGeneration { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AIResumeProviderResult<ResumeAnalysis>> AnalyzeAsync(AnalyzeResumeRequest request, CancellationToken cancellationToken = default)
        {
            AnalysisContactName = request.Source.Contact.Name;
            if (AnalysisFailure is not null) throw AnalysisFailure;
            return Task.FromResult(new AIResumeProviderResult<ResumeAnalysis>(AnalysisOverride ?? new(87, [".NET"], [], ["Kubernetes"], ["APIs"], [".NET experience"], ["Kubernetes"], ["Highlight API work"]), "claude-haiku-4-5", 10, 5));
        }
        public async Task<AIResumeProviderResult<TailoredResumeContent>> GenerateTailoredResumeAsync(GenerateTailoredResumeRequest request, CancellationToken cancellationToken = default)
        {
            GenerationCalls++; GenerationStarted.TrySetResult();
            GenerationSourceJson = JsonSerializer.Serialize(request.Source, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            OnGeneration?.Invoke();
            if (GenerationFailure is not null) throw GenerationFailure;
            if (BlockGeneration) await ContinueGeneration.Task.WaitAsync(cancellationToken);
            return new(GenerationOverride ?? request.Source, "claude-haiku-4-5", 20, 10);
        }
        public Task<TailoredResumeValidation> ValidateTailoredResumeAsync(ValidateTailoredResumeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AIResumeContentGuard.Validate(request.Source, request.Generated));
    }

    private sealed class SourceSnapshotLogger(Action beforePersistence) : ILogger<AIResumeService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (id.Id == 7408) return;
            Assert.Equal(LogLevel.Information, level);
            Assert.Equal(7405, id.Id);
            Assert.Equal("AIResumeSourceSnapshot", id.Name);
            Assert.Null(exception);
            beforePersistence();
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakePhonePeGateway : IPhonePeGateway
    {
        public long LastAmountInMinorUnits { get; private set; }
        public string? OrderId { get; private set; }
        public PhonePeOrderStateKind State { get; set; } = PhonePeOrderStateKind.Pending;
        public long? AmountOverride { get; set; }
        public Task<PhonePeCheckout> CreateCheckoutAsync(string merchantOrderId, long amountInMinorUnits, CancellationToken cancellationToken = default)
        { OrderId = merchantOrderId; LastAmountInMinorUnits = amountInMinorUnits; return Task.FromResult(new PhonePeCheckout("https://phonepe.test/checkout")); }
        public Task<PhonePeOrderState> GetOrderStatusAsync(string merchantOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PhonePeOrderState(State, merchantOrderId, "txn-test", AmountOverride ?? LastAmountInMinorUnits));
        public bool VerifyWebhookAuthorization(string authorization) => authorization == "signed-fixture";
        public PhonePeCallback ParseCallback(ReadOnlyMemory<byte> rawBody) => new(OrderId ?? "", State, "event-test");
    }
}
