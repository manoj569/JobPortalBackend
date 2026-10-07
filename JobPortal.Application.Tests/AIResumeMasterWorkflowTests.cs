using System.Text.Json;
using System.Reflection;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AIResume;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIResume;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIResumeMasterWorkflowTests
{
    [Fact]
    public async Task AmbiguousSessionCommitDoesNotDeletePersistedMaster()
    {
        await using var fixture = await Workflow.Create();
        fixture.CommitFault.ThrowAfterNextSave = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Service.CreateSessionAsync(fixture.UserId,
            new(fixture.SourceId, null, MasterResumeFixture.Jd), default));
        await using var reloaded = new JobPortalDbContext(fixture.DbOptions);
        var session = await reloaded.AIResumeSessions.SingleAsync();
        var master = JsonSerializer.Deserialize<ResumeSessionEvidence>(session.EvidenceJson, fixture.Json)!.MasterDocument;
        Assert.True(fixture.Storage.Files.ContainsKey(master.StorageKey));
        Assert.Equal("original_docx", (await fixture.CreateService(new AIResumeRepository(reloaded)).GetSessionAsync(fixture.UserId, session.Id, default)).DocumentCapabilities!.PreservationMode);
        Assert.Equal(0, fixture.Handler.Calls);
        Assert.Empty(reloaded.AIResumeCreditTransactions);
    }

    [Fact]
    public async Task MissingLegacySourceReturnsActionableReuploadError()
    {
        await using var fixture = await Workflow.Create();
        fixture.Storage.Files.Remove("owned.docx");

        var error = await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.CreateSessionAsync(
            fixture.UserId, new(fixture.SourceId, null, MasterResumeFixture.Jd), default));

        Assert.Equal("resume_source_reupload_required", error.Code);
        Assert.Equal("Please upload your resume again before using AI Resume. Your existing resume was uploaded before document-preserving AI Resume support was enabled.", error.Message);
        Assert.Empty(fixture.Db.AIResumeSessions);
    }

    [Fact]
    public async Task CorruptSourceKeepsInvalidSourceErrorInsteadOfReuploadError()
    {
        await using var fixture = await Workflow.Create();
        fixture.Storage.Files["owned.docx"] = "not a DOCX package"u8.ToArray();

        var error = await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.CreateSessionAsync(
            fixture.UserId, new(fixture.SourceId, null, MasterResumeFixture.Jd), default));

        Assert.Equal("invalid_resume_source", error.Code);
        Assert.Empty(fixture.Db.AIResumeSessions);
    }

    [Fact]
    public async Task EightSafeTwoUnsafePersistOriginalFormatAndConsumeExactlyOneCredit()
    {
        await using var fixture = await Workflow.Create();
        var session = await fixture.Service.CreateSessionAsync(fixture.UserId, new(fixture.SourceId, null, MasterResumeFixture.Jd), default);
        Assert.Equal("original_docx", session.DocumentCapabilities!.PreservationMode);
        var master = JsonSerializer.Deserialize<ResumeSessionEvidence>(fixture.Db.AIResumeSessions.Single().EvidenceJson, fixture.Json)!.MasterDocument;
        var sourceJson = fixture.Db.AIResumeSessions.Single().SourceJson;
        var original = fixture.Storage.Files["owned.docx"].ToArray();
        await fixture.Service.AnalyzeAsync(fixture.UserId, session.Id, default);
        fixture.Storage.Files.Remove("owned.docx"); // A later candidate upload/delete cannot invalidate this session.
        var key = Guid.NewGuid();
        var result = await fixture.Service.GenerateAsync(fixture.UserId, session.Id, new(key), default);
        Assert.Equal(result.Id, (await fixture.Service.GenerateAsync(fixture.UserId, session.Id, new(key), default)).Id);
        Assert.Equal(8, result.Tailoring!.Replacements.Count(x => x.Status == "accepted"));
        Assert.Equal(2, result.Tailoring.Replacements.Count(x => x.Status == "rejected"));
        Assert.Equal(5, result.Content.Experience.Length); Assert.Equal(5, result.Content.Projects.Length);
        Assert.Equal(MasterResumeFixture.Source.ProfessionalSummary, result.Content.ProfessionalSummary);
        Assert.Equal(sourceJson, fixture.Db.AIResumeSessions.Single().SourceJson);
        Assert.Equal(original, fixture.Storage.Files[master.StorageKey]);
        var wallet = await fixture.Service.CreditsAsync(fixture.UserId, default);
        Assert.Equal(0, wallet.Balance); Assert.Equal(0, wallet.Reserved); Assert.Equal(1, wallet.LifetimeConsumed);
        Assert.Equal(1, await fixture.Db.AIResumeCreditTransactions.CountAsync(x => x.Kind == AIResumeCreditKind.Consumption));
        Assert.Equal(1, fixture.GenerationCalls); Assert.Equal(2, fixture.Handler.Calls);
        var download = await fixture.Service.DownloadAsync(fixture.UserId, result.Id, "original", default);
        Assert.EndsWith(".docx", download.FileName);
        Assert.Contains("Engineering:", System.Text.Encoding.UTF8.GetString(MasterResumeFixture.Parts(download.Content)["word/document.xml"]));
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.DownloadAsync(Guid.NewGuid(), result.Id, "docx", default));
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.DownloadAsync(fixture.UserId, result.Id, "pdf", default));
        var responseJson = JsonSerializer.Serialize(result, fixture.Json);
        Assert.DoesNotContain("storageKey", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(master.StorageKey, responseJson);
        foreach (var message in fixture.Logger.Messages)
            foreach (var sensitive in new[] { "private-key", "private-workspace", MasterResumeFixture.Source.Contact.Email,
                MasterResumeFixture.Source.Contact.Phone, MasterResumeFixture.Text, MasterResumeFixture.Jd }) Assert.DoesNotContain(sensitive, message);
    }

    [Theory]
    [InlineData("zero_safe")]
    [InlineData("provider")]
    [InlineData("storage")]
    [InlineData("cancel")]
    [InlineData("persistence")]
    public async Task FailedTailoringRestoresCreditAndPersistsNoResumeOrArtifact(string failure)
    {
        await using var fixture = await Workflow.Create();
        var session = await fixture.Service.CreateSessionAsync(fixture.UserId, new(fixture.SourceId, null, MasterResumeFixture.Jd), default);
        await fixture.Service.AnalyzeAsync(fixture.UserId, session.Id, default);
        if (failure == "zero_safe") fixture.Patch = new(MasterResumeFixture.Patch.Replacements.Where(x => x.Reason.StartsWith("Unsafe", StringComparison.Ordinal)).ToArray(), []);
        if (failure == "provider") fixture.FailProvider = true;
        if (failure == "storage") fixture.Storage.FailWrites = true;
        if (failure == "persistence") fixture.RepositoryFault.FailAtWrite = fixture.RepositoryFault.Writes + 2;
        using var cancellation = new CancellationTokenSource();
        if (failure == "cancel") fixture.OnGeneration = () => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.GenerateAsync(fixture.UserId, session.Id, new(Guid.NewGuid()), cancellation.Token));
        var wallet = await fixture.Service.CreditsAsync(fixture.UserId, default);
        Assert.Equal(1, wallet.Balance); Assert.Equal(0, wallet.Reserved); Assert.Equal(0, wallet.LifetimeConsumed);
        Assert.Empty(fixture.Db.TailoredResumes);
        Assert.Equal(1, await fixture.Db.AIResumeCreditTransactions.CountAsync(x => x.Kind == AIResumeCreditKind.Release));
        Assert.Equal(2, fixture.Storage.Files.Count); // Original upload + frozen master only.
        Assert.Equal(1, fixture.GenerationCalls);
        if (failure == "zero_safe")
        {
            Assert.Contains(fixture.Logger.Messages, x => x.Contains(
                "AIResumeTailoringPatch Proposed=2 Accepted=0 Rejected=2 PreservationMode=original_docx", StringComparison.Ordinal));
            Assert.Contains(fixture.Logger.Messages, x => x.Contains(
                "AIResumeGenerationRejected Code=no_safe_tailoring_changes Stage=patch_guard_result", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task MasterSnapshotArtifactAndReviewSurviveFreshContextReload()
    {
        await using var fixture = await Workflow.Create();
        var session = await fixture.Service.CreateSessionAsync(fixture.UserId, new(fixture.SourceId, null, MasterResumeFixture.Jd), default);
        await fixture.Service.AnalyzeAsync(fixture.UserId, session.Id, default);
        var result = await fixture.Service.GenerateAsync(fixture.UserId, session.Id, new(Guid.NewGuid()), default);
        var download = await fixture.Service.DownloadAsync(fixture.UserId, result.Id, "original", default);
        fixture.Storage.Files.Remove("owned.docx");
        await using var reloadedDb = new JobPortalDbContext(fixture.DbOptions);
        var reloaded = fixture.CreateService(new AIResumeRepository(reloadedDb));
        var snapshot = JsonSerializer.Deserialize<TailoredResumeContent>((await reloadedDb.AIResumeSessions.SingleAsync()).SourceJson, fixture.Json)!;
        Assert.Equal(15, snapshot.Skills.Length);
        Assert.Equal(5, snapshot.Experience.Length); Assert.Equal(5, snapshot.Projects.Length); Assert.Equal(2, snapshot.Education.Length);
        Assert.Contains(ResumeEvidenceCatalog.Create(snapshot), x => x.Id == "SRC-EXP-001-BULLET-001");
        var fetched = await reloaded.GetResumeAsync(fixture.UserId, result.Id, default);
        Assert.Equal(8, fetched.Tailoring!.Replacements.Count(x => x.Status == "accepted"));
        Assert.Equal(download.Content, (await reloaded.DownloadAsync(fixture.UserId, result.Id, "original", default)).Content);
        fetched = await reloaded.ReviewReplacementAsync(fixture.UserId, result.Id, "SRC-EXP-001-BULLET-001", new(0, "reset"), default);
        Assert.Equal(snapshot.Experience[0].Bullets[0], fetched.Content.Experience[0].Bullets[0]);
        Assert.Equal(1, fixture.GenerationCalls);
    }

    [Fact]
    public async Task CompatibleFullContentEditCannotChangeIdentityOrAddTechnology()
    {
        await using var fixture = await Workflow.Create();
        var session = await fixture.Service.CreateSessionAsync(fixture.UserId, new(fixture.SourceId, null, MasterResumeFixture.Jd), default);
        await fixture.Service.AnalyzeAsync(fixture.UserId, session.Id, default);
        var generated = await fixture.Service.GenerateAsync(fixture.UserId, session.Id, new(Guid.NewGuid()), default);
        var experience = generated.Content.Experience.ToArray();
        experience[0] = experience[0] with { Employer = "Invented Employer" };
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.EditAsync(fixture.UserId, generated.Id,
            new(0, generated.Content with { Experience = experience }), default));
        experience[0] = generated.Content.Experience[0] with { Bullets = ["Developed CUDA APIs.", generated.Content.Experience[0].Bullets[1]] };
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.EditAsync(fixture.UserId, generated.Id,
            new(0, generated.Content with { Experience = experience }), default));
        experience[0] = generated.Content.Experience[0] with { Bullets = [MasterResumeFixture.Source.Experience[0].Bullets[0], generated.Content.Experience[0].Bullets[1]] };
        var edited = await fixture.Service.EditAsync(fixture.UserId, generated.Id, new(0, generated.Content with { Experience = experience }), default);
        Assert.Equal(MasterResumeFixture.Source.Experience[0].Bullets[0], edited.Content.Experience[0].Bullets[0]);
        Assert.Equal(1, fixture.GenerationCalls);
        Assert.Equal(1, (await fixture.Service.CreditsAsync(fixture.UserId, default)).LifetimeConsumed);
    }

    [Fact]
    public async Task ReviewEditResetAreFreeSafeAndRegenerationUsesImmutableMaster()
    {
        await using var fixture = await Workflow.Create();
        var session = await fixture.Service.CreateSessionAsync(fixture.UserId, new(fixture.SourceId, null, MasterResumeFixture.Jd), default);
        await fixture.Service.AnalyzeAsync(fixture.UserId, session.Id, default);
        var result = await fixture.Service.GenerateAsync(fixture.UserId, session.Id, new(Guid.NewGuid()), default);
        var id = "SRC-EXP-001-BULLET-001";
        var original = MasterResumeFixture.Source.Experience[0].Bullets[0];
        result = await fixture.Service.ReviewReplacementAsync(fixture.UserId, result.Id, id, new(0, "original"), default);
        Assert.Equal(original, result.Content.Experience[0].Bullets[0]);
        result = await fixture.Service.ReviewReplacementAsync(fixture.UserId, result.Id, id, new(1, "accept"), default);
        Assert.StartsWith("Developed", result.Content.Experience[0].Bullets[0]);
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.ReviewReplacementAsync(fixture.UserId, result.Id, id, new(2, "edit", "Built CUDA APIs."), default));
        result = await fixture.Service.ReviewReplacementAsync(fixture.UserId, result.Id, id, new(2, "edit", original.Replace("Built", "Implemented", StringComparison.Ordinal)), default);
        Assert.StartsWith("Implemented", result.Content.Experience[0].Bullets[0]);
        result = await fixture.Service.ReviewReplacementAsync(fixture.UserId, result.Id, id, new(3, "reset"), default);
        Assert.Equal(original, result.Content.Experience[0].Bullets[0]);
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.ReviewReplacementAsync(fixture.UserId, result.Id, "SRC-SUMMARY-001", new(4, "accept"), default));
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.ReviewReplacementAsync(fixture.UserId, result.Id, id, new(0, "reset"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.ReviewReplacementAsync(Guid.NewGuid(), result.Id, id, new(4, "reset"), default));
        Assert.Equal(1, fixture.GenerationCalls);
        Assert.Equal(1, (await fixture.Service.CreditsAsync(fixture.UserId, default)).LifetimeConsumed);
        Assert.Equal(4, fixture.Db.TailoredResumeEdits.Count());
        var wallet = fixture.Db.AIResumeCreditWallets.Single(); wallet.Balance++; wallet.LifetimePurchased++;
        await fixture.Db.SaveChangesAsync();
        fixture.Storage.Files.Remove("owned.docx");
        var regenerated = await fixture.Service.GenerateAsync(fixture.UserId, session.Id, new(Guid.NewGuid()), default);
        Assert.Equal(2, regenerated.Version); Assert.Equal(2, fixture.GenerationCalls);
        Assert.Equal(2, (await fixture.Service.CreditsAsync(fixture.UserId, default)).LifetimeConsumed);
        Assert.True(fixture.GenerationSources.All(x => x == fixture.Db.AIResumeSessions.Single().SourceJson));
    }

    [Fact]
    public async Task PdfFallbackIsExplicitAndNeverClaimsOriginalLayout()
    {
        await using var fixture = await Workflow.Create();
        var pdf = new ProfessionalResumeDocumentRenderer().Render(MasterResumeFixture.Source, "pdf").Content;
        fixture.Storage.Files["owned.pdf"] = pdf;
        var user = fixture.Db.Users.Single(); user.ResumeStorageKey = "owned.pdf"; user.ResumeFileName = "master.pdf";
        await fixture.Db.SaveChangesAsync();
        var session = await fixture.Service.CreateSessionAsync(fixture.UserId, new(fixture.SourceId, null, MasterResumeFixture.Jd), default);
        Assert.False(session.DocumentCapabilities!.LayoutPreserved);
        Assert.Equal("rendered_fallback", session.DocumentCapabilities.PreservationMode);
        await fixture.Service.AnalyzeAsync(fixture.UserId, session.Id, default);
        // Select known factual targets from this PDF extraction; a rewrite retains their exact vocabulary.
        var source = JsonSerializer.Deserialize<TailoredResumeContent>(fixture.Db.AIResumeSessions.Single().SourceJson, fixture.Json)!;
        fixture.Patch = new([MasterResumeFixture.Change(ResumePatchGuard.Targets(source).First(x => x.Scope.StartsWith("experience/", StringComparison.Ordinal)))], []);
        var result = await fixture.Service.GenerateAsync(fixture.UserId, session.Id, new(Guid.NewGuid()), default);
        Assert.Equal("rendered_fallback", result.DocumentCapabilities!.PreservationMode);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await fixture.Service.DownloadAsync(fixture.UserId, result.Id, "original", default)).Content));
    }

    private sealed class Workflow : IAsyncDisposable
    {
        internal JobPortalDbContext Db { get; private init; } = null!;
        internal DbContextOptions<JobPortalDbContext> DbOptions { get; private init; } = null!;
        internal AIResumeWorkflowTests.FailingWriteRepository RepositoryFault { get; private set; } = null!;
        internal CommitFailureInterceptor CommitFault { get; private init; } = null!;
        private ClaudeAIResumeProvider Provider { get; set; } = null!;
        internal MasterResumeMemoryStorage Storage { get; } = new();
        internal Guid UserId { get; private init; }
        internal Guid SourceId { get; private init; }
        internal AIResumeService Service { get; private set; } = null!;
        internal ClaudeAIResumeProviderTests.Handler Handler { get; private set; } = null!;
        internal TailoringPatch Patch { get; set; } = MasterResumeFixture.Patch;
        internal bool FailProvider { get; set; }
        internal Action? OnGeneration { get; set; }
        internal int GenerationCalls { get; private set; }
        internal List<string> GenerationSources { get; } = [];
        internal CaptureLogger Logger { get; } = new();
        internal JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
        internal static async Task<Workflow> Create()
        {
            var commitFault = new CommitFailureInterceptor();
            var dbOptions = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(commitFault).Options;
            var db = new JobPortalDbContext(dbOptions);
            var role = new Role { Name = "Candidate", NormalizedName = "CANDIDATE" };
            var user = new User { Email = "candidate@example.invalid", NormalizedEmail = "CANDIDATE@EXAMPLE.INVALID", FirstName = "Account", LastName = "Fixture",
                Role = role, RoleId = role.Id, Status = UserStatus.Active, ResumeStorageKey = "owned.docx", ResumeFileName = "master.docx" };
            user.ResumeProfile = new CandidateResumeProfile { UserId = user.Id, User = user };
            db.Roles.Add(role); db.Users.Add(user); db.AIResumeCreditWallets.Add(new AIResumeCreditWallet { UserId = user.Id, Balance = 1, LifetimePurchased = 1 });
            await db.SaveChangesAsync();
            var fixture = new Workflow { Db = db, DbOptions = dbOptions, CommitFault = commitFault, UserId = user.Id, SourceId = user.ResumeProfile.Id };
            fixture.Handler = new ClaudeAIResumeProviderTests.Handler(async (request, ct) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                if (body.RootElement.GetProperty("max_tokens").GetInt32() == 1200) return ClaudeAIResumeProviderTests.Response(MasterResumeFixture.Analysis);
                fixture.GenerationCalls++;
                using var input = JsonDocument.Parse(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!);
                fixture.GenerationSources.Add(input.RootElement.GetProperty("source").GetRawText());
                fixture.OnGeneration?.Invoke();
                ct.ThrowIfCancellationRequested();
                if (fixture.FailProvider) throw new TaskCanceledException("Provider timeout fixture");
                return ClaudeAIResumeProviderTests.Response(fixture.Patch);
            });
            var settings = Options.Create(new AIResumeOptions { ApiKey = "private-key", WorkspaceId = "private-workspace" });
            fixture.Provider = new ClaudeAIResumeProvider(new ClaudeAIResumeProviderTests.Factory(fixture.Handler), settings, new ClaudeAIResumeProviderTests.CapturingLogger(), new ClaudeAIResumeProviderTests.DevelopmentEnvironment());
            var repository = DispatchProxy.Create<IAIResumeRepository, AIResumeWorkflowTests.FailingWriteRepository>();
            fixture.RepositoryFault = (AIResumeWorkflowTests.FailingWriteRepository)(object)repository;
            fixture.RepositoryFault.Inner = new AIResumeRepository(db);
            fixture.Service = fixture.CreateService(repository);
            return fixture;
        }
        internal AIResumeService CreateService(IAIResumeRepository repository)
        {
            var renderer = new ProfessionalResumeDocumentRenderer();
            return new AIResumeService(repository, Provider, new StructuredResumeSourceParser(Storage, new ResumeTextExtractor()), renderer,
                null!, Options.Create(new AIResumeOptions { ApiKey = "private-key", WorkspaceId = "private-workspace" }), TimeProvider.System, Logger,
                new ClaudeAIResumeProviderTests.DevelopmentEnvironment(), new OriginalResumeDocuments(Storage, renderer));
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class CommitFailureInterceptor : SaveChangesInterceptor
    {
        internal bool ThrowAfterNextSave { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (ThrowAfterNextSave) { ThrowAfterNextSave = false; throw new DbUpdateException("Ambiguous commit fixture"); }
            return new(result);
        }
    }
    private sealed class CaptureLogger : ILogger<AIResumeService>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
