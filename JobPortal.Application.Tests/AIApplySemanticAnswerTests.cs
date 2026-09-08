using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIApplySemanticAnswerTests
{
    [Theory]
    [InlineData("How many years of C# experience do you have?")]
    [InlineData("How long have you worked with C Sharp?")]
    public void EquivalentSkillQuestionsHaveSameCanonicalKey(string text)
    {
        Assert.Equal("experience.skill.csharp", Classifier().Analyze(new(text, "Text")).CanonicalKey);
    }

    [Fact]
    public void DifferentSkillsNeverShareCanonicalKey()
    {
        var classifier = Classifier();
        Assert.NotEqual(classifier.Analyze(new("How many years of C# experience?", "Text")).CanonicalKey,
            classifier.Analyze(new("How many years of Java experience?", "Text")).CanonicalKey);
    }

    [Fact]
    public async Task VerifiedCanonicalSkillAnswerIsReusedWithoutAi()
    {
        await using var fixture = await Fixture.Create(new UserApplicationAnswer { Question = "How many years of C# experience?", NormalizedQuestion = "how many years of c experience", Answer = "4 years", IsActive = true, IsVerified = true, Source = AIApplyAnswerSource.UserVerified });
        var result = await fixture.Resolver.ResolveAsync(fixture.UserId, new("How long have you worked with CSharp?", "Text"), false, default);
        Assert.True(result.CanAutoUse); Assert.Equal("4 years", result.Answer); Assert.Equal(ApplicationAnswerResolutionSource.UserVerifiedCanonical, result.Source); Assert.Equal(0, fixture.LanguageModel.Calls);
    }

    [Fact]
    public async Task SensitiveQuestionCannotUseSemanticOrGeneratedGuess()
    {
        await using var fixture = await Fixture.Create(new UserApplicationAnswer { Question = "Do you need employer support?", NormalizedQuestion = "do you need employer support", Answer = "No", IsActive = true, IsVerified = true, Source = AIApplyAnswerSource.UserVerified }, aiEnabled: true, semanticConfidence: .99m);
        var result = await fixture.Resolver.ResolveAsync(fixture.UserId, new("Will you now or in future require visa sponsorship?", "Text"), true, default);
        Assert.False(result.CanAutoUse); Assert.Equal(ApplicationAnswerResolutionSource.None, result.Source); Assert.Equal(0, fixture.LanguageModel.Calls);
    }

    [Fact]
    public async Task LowConfidenceSemanticMatchAsksUser()
    {
        await using var fixture = await Fixture.Create(new UserApplicationAnswer { Question = "Describe a challenge", NormalizedQuestion = "describe a challenge", Answer = "Example", IsActive = true, IsVerified = true, Source = AIApplyAnswerSource.UserVerified }, aiEnabled: true, semanticConfidence: .73m);
        var result = await fixture.Resolver.ResolveAsync(fixture.UserId, new("Describe your approach", "Text"), false, default);
        Assert.False(result.CanAutoUse);
    }

    [Fact]
    public async Task HighConfidenceLowRiskSemanticMatchIsReusable()
    {
        await using var fixture = await Fixture.Create(new UserApplicationAnswer { Question = "Describe your approach", NormalizedQuestion = "describe your approach", Answer = "A concise verified answer", IsActive = true, IsVerified = true, Source = AIApplyAnswerSource.UserVerified }, aiEnabled: true, semanticConfidence: .98m);
        var result = await fixture.Resolver.ResolveAsync(fixture.UserId, new("Explain your approach", "Textarea"), false, default);
        Assert.True(result.CanAutoUse); Assert.Equal(ApplicationAnswerResolutionSource.UserVerifiedSemantic, result.Source);
    }

    [Fact]
    public async Task CompanyScopedAnswerIsNotReusedForDifferentCompany()
    {
        await using var fixture = await Fixture.Create(new UserApplicationAnswer { Question = "Why do you want to work at Company A?", NormalizedQuestion = "why do you want to work at company a", Answer = "A answer", IsActive = true, IsVerified = true, Source = AIApplyAnswerSource.UserVerified }, aiEnabled: true, semanticConfidence: .99m);
        var result = await fixture.Resolver.ResolveAsync(fixture.UserId, new("Why do you want to work at Company B?", "Textarea"), false, default);
        Assert.False(result.CanAutoUse);
    }

    [Fact]
    public async Task ProfileEmailAvoidsLanguageModel()
    {
        await using var fixture = await Fixture.Create(email: "candidate@example.test", aiEnabled: true);
        var result = await fixture.Resolver.ResolveAsync(fixture.UserId, new("What is your email address?", "Email"), true, default);
        Assert.Equal("candidate@example.test", result.Answer); Assert.Equal(ApplicationAnswerResolutionSource.ProfileDerived, result.Source); Assert.Equal(0, fixture.LanguageModel.Calls);
    }

    [Fact]
    public async Task SubjectiveGenerationAlwaysRequiresConfirmation()
    {
        await using var fixture = await Fixture.Create(aiEnabled: true, generated: "I am interested in this role because it aligns with my verified skills.");
        var result = await fixture.Resolver.ResolveAsync(fixture.UserId, new("Why are you interested in this role?", "Textarea"), true, default);
        Assert.Equal(ApplicationAnswerResolutionSource.AIGenerated, result.Source); Assert.True(result.RequiresUserConfirmation); Assert.False(result.CanAutoUse); Assert.Equal(1, fixture.LanguageModel.Calls); Assert.Equal(1, result.Usage!.AIRequests);
    }

    [Fact]
    public async Task DisabledAiAndProviderFailureSafelyAskUser()
    {
        await using var disabled = await Fixture.Create();
        Assert.False((await disabled.Resolver.ResolveAsync(disabled.UserId, new("Why this role?", "Textarea"), true, default)).CanAutoUse);
        Assert.Equal(0, disabled.LanguageModel.Calls);
        await using var failing = await Fixture.Create(aiEnabled: true, throwProvider: true);
        Assert.False((await failing.Resolver.ResolveAsync(failing.UserId, new("Why this role?", "Textarea"), true, default)).CanAutoUse);
    }

    private static DeterministicApplicationQuestionClassifier Classifier() => new(new ApplicationQuestionMatcher(null!));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly JobPortalDbContext context; public Guid UserId { get; } public ApplicationAnswerResolver Resolver { get; } public FakeLanguageModel LanguageModel { get; }
        private Fixture(JobPortalDbContext context, Guid userId, ApplicationAnswerResolver resolver, FakeLanguageModel languageModel) { this.context = context; UserId = userId; Resolver = resolver; LanguageModel = languageModel; }
        public static async Task<Fixture> Create(UserApplicationAnswer? answer = null, string? email = null, bool aiEnabled = false, decimal semanticConfidence = 0, string? generated = null, bool throwProvider = false)
        {
            var context = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options); var userId = Guid.NewGuid();
            context.Users.Add(new User { Id = userId, Email = email ?? "person@example.test", Status = UserStatus.Active });
            if (answer is not null) { answer.UserId = userId; context.UserApplicationAnswers.Add(answer); } await context.SaveChangesAsync();
            var repository = new AIApplyRepository(context); var matcher = new ApplicationQuestionMatcher(repository); var classifier = new DeterministicApplicationQuestionClassifier(matcher); var language = new FakeLanguageModel(generated, throwProvider);
            var options = Options.Create(new AIApplyOptions { AI = new() { Enabled = aiEnabled, SemanticMatchingEnabled = aiEnabled, AnswerGenerationEnabled = aiEnabled, SemanticMatchThreshold = .9m, AutoUseThreshold = .95m, GeneratedAnswerThreshold = .95m } });
            var semantic = new FakeSemanticMatcher(answer?.Id, semanticConfidence); var resolver = new ApplicationAnswerResolver(repository, classifier, semantic, new AIApplicationAnswerPolicy(), language, options);
            return new(context, userId, resolver, language);
        }
        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
    private sealed class FakeSemanticMatcher(Guid? id, decimal confidence) : IApplicationSemanticMatcher { public Task<SemanticMatchResult> FindBestMatchAsync(ApplicationQuestionAnalysis question, IReadOnlyCollection<UserApplicationAnswer> candidates, CancellationToken ct) => Task.FromResult(new SemanticMatchResult(id, confidence, confidence, "fake")); }
    private sealed class FakeLanguageModel(string? answer, bool throws) : IAIApplyLanguageModel
    {
        public int Calls { get; private set; }
        public Task<AIApplyLanguageModelResult?> GenerateAsync(AIApplyLanguageModelRequest request, CancellationToken ct) { Calls++; if (throws) throw new TimeoutException(); return Task.FromResult<AIApplyLanguageModelResult?>(answer is null ? null : new(answer, .99m, true, 10, 5)); }
    }
}
