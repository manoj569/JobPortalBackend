using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class AcceleratorInterviewCatalogService(IAcceleratorStore writes, IAcceleratorPhase5Store store,
    IAcceleratorPhase4Store competencies, AcceleratorService access, IAuditWriter audit, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<Guid> CreateDefinitionAsync(Guid adminId, CreateInterviewDefinitionRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var key = request.StableKey?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 120 || key.Any(x => !char.IsAsciiLetterLower(x) && !char.IsAsciiDigit(x) && x != '-') ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.Description?.Length > 2000)
            throw Invalid("Invalid interview definition.");
        var role = await writes.RoleAsync(request.TargetRoleId, ct) ?? throw new NotFoundException("Target role not found.");
        var existing = await store.DefinitionByKeyAsync(key, ct);
        if (existing is not null)
        {
            if (existing.TargetRoleId != role.Id) throw new ConflictException("Interview stable key belongs to another role.");
            return existing.Id;
        }
        var row = new AcceleratorInterviewDefinition { StableKey = key, Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty, TargetRoleId = role.Id };
        store.Add(row); await AdminAudit(adminId, AuditAction.Create, "AcceleratorInterviewDefinition", row.Id, ct);
        return row.Id;
    }, ct);

    public Task<InterviewDraftResponse> CreateDraftAsync(Guid adminId, CreateInterviewDraftRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        if (request.DefinitionId == Guid.Empty || request.Version < 1) throw Invalid("Interview definition and version are required.");
        ValidateMetadata(request.Type, request.Difficulty, request.EstimatedMinutes, request.TargetExperienceLevel, request.Instructions);
        _ = await store.DefinitionAsync(request.DefinitionId, ct) ?? throw new NotFoundException("Interview definition not found.");
        if (await store.VersionByNumberAsync(request.DefinitionId, request.Version, ct) is not null)
            throw new ConflictException("Interview version already exists.");
        var row = new AcceleratorInterviewVersion { DefinitionId = request.DefinitionId, Version = request.Version,
            Type = request.Type, Difficulty = request.Difficulty, EstimatedMinutes = request.EstimatedMinutes,
            TargetExperienceLevel = request.TargetExperienceLevel.Trim(), Instructions = request.Instructions.Trim() };
        store.Add(row); await AdminAudit(adminId, AuditAction.Create, "AcceleratorInterviewVersion", row.Id, ct);
        return new InterviewDraftResponse(row.Id, row.Revision);
    }, ct);

    public Task<InterviewDraftResponse> EditDraftAsync(Guid adminId, Guid versionId, EditInterviewDraftRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var row = await EditableDraft(versionId, request.ExpectedRevision, ct);
        ValidateMetadata(request.Type, request.Difficulty, request.EstimatedMinutes, request.TargetExperienceLevel, request.Instructions);
        row.Type = request.Type; row.Difficulty = request.Difficulty; row.EstimatedMinutes = request.EstimatedMinutes;
        row.TargetExperienceLevel = request.TargetExperienceLevel.Trim(); row.Instructions = request.Instructions.Trim();
        row.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorInterviewVersion", row.Id, ct);
        return new InterviewDraftResponse(row.Id, row.Revision);
    }, ct);

    public Task<Guid> UpsertCompetencyAsync(Guid adminId, Guid versionId, UpsertInterviewCompetencyRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await EditableDraft(versionId, request.ExpectedRevision, ct);
        if (request.ExpectedProficiency is < 0 or > 4 || request.Weight <= 0 || request.Weight > 100 ||
            request.QuestionCoverageTarget is < 0 or > 100) throw Invalid("Invalid interview competency mapping.");
        await ValidateCompetency(version, request.CompetencyId, ct);
        var row = version.Competencies.SingleOrDefault(x => x.CompetencyId == request.CompetencyId);
        if (row is null)
        {
            row = new AcceleratorInterviewCompetency { InterviewVersionId = version.Id, CompetencyId = request.CompetencyId };
            version.Competencies.Add(row);
        }
        row.ExpectedProficiency = request.ExpectedProficiency; row.Weight = request.Weight;
        row.IsRequired = request.IsRequired; row.QuestionCoverageTarget = request.QuestionCoverageTarget;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorInterviewCompetency", row.Id, ct);
        return row.Id;
    }, ct);

    public Task<Guid> UpsertSectionAsync(Guid adminId, Guid versionId, UpsertInterviewSectionRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await EditableDraft(versionId, request.ExpectedRevision, ct);
        if (request.Sequence < 1 || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 ||
            !Enum.IsDefined(request.Type) || request.EstimatedMinutes is < 1 or > 1000 ||
            request.QuestionCount is < 1 or > 50 || request.MaxFollowUpsPerQuestion is < 0 or > 3)
            throw Invalid("Invalid interview section.");
        var row = request.Id.HasValue ? version.Sections.SingleOrDefault(x => x.Id == request.Id.Value)
            ?? throw new NotFoundException("Interview section not found.") : new AcceleratorInterviewSection { InterviewVersionId = version.Id };
        if (!request.Id.HasValue) version.Sections.Add(row);
        if (version.Sections.Any(x => x.Id != row.Id && x.Sequence == request.Sequence)) throw new ConflictException("Section sequence already exists.");
        row.Sequence = request.Sequence; row.Title = request.Title.Trim(); row.Type = request.Type;
        row.EstimatedMinutes = request.EstimatedMinutes; row.IsRequired = request.IsRequired;
        row.QuestionCount = request.QuestionCount; row.MaxFollowUpsPerQuestion = request.MaxFollowUpsPerQuestion;
        row.AllowStaticFallback = request.AllowStaticFallback;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorInterviewSection", row.Id, ct);
        return row.Id;
    }, ct);

    public Task<Guid> UpsertQuestionAsync(Guid adminId, Guid versionId, UpsertInterviewQuestionRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await EditableDraft(versionId, request.ExpectedRevision, ct);
        var section = version.Sections.SingleOrDefault(x => x.Id == request.SectionId)
            ?? throw new NotFoundException("Interview section not found.");
        if (request.Sequence < 1 || !Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Type) ||
            !Enum.IsDefined(request.EvaluationMode) || !Enum.IsDefined(request.Difficulty) ||
            string.IsNullOrWhiteSpace(request.PublicPrompt) || request.PublicPrompt.Length > 12000 ||
            request.HiddenEvaluatorGuidance?.Length > 8000 || request.FollowUpPrompt?.Length > 8000 ||
            request.DeterministicRulesJson?.Length > 16000 || request.ExpectedConceptsJson?.Length > 16000)
            throw Invalid("Invalid interview question.");
        ValidateJson(request.DeterministicRulesJson ?? "[]"); ValidateJson(request.ExpectedConceptsJson ?? "[]");
        if (request.CompetencyId.HasValue) await ValidateCompetency(version, request.CompetencyId.Value, ct);
        var row = request.Id.HasValue ? section.Questions.SingleOrDefault(x => x.Id == request.Id.Value)
            ?? throw new NotFoundException("Interview question not found in section.") : new AcceleratorInterviewQuestion { SectionId = section.Id };
        if (!request.Id.HasValue) section.Questions.Add(row);
        if (section.Questions.Any(x => x.Id != row.Id && x.Sequence == request.Sequence)) throw new ConflictException("Question sequence already exists.");
        row.Sequence = request.Sequence; row.CompetencyId = request.CompetencyId; row.Kind = request.Kind;
        row.Type = request.Type; row.EvaluationMode = request.EvaluationMode; row.Difficulty = request.Difficulty;
        row.PublicPrompt = request.PublicPrompt.Trim(); row.ExpectedConceptsJson = request.ExpectedConceptsJson ?? "[]";
        row.HiddenEvaluatorGuidance = request.HiddenEvaluatorGuidance?.Trim() ?? string.Empty;
        row.DeterministicRulesJson = request.DeterministicRulesJson ?? "[]";
        row.FollowUpPrompt = request.FollowUpPrompt?.Trim() ?? string.Empty; row.IsActive = request.IsActive;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorInterviewQuestion", row.Id, ct);
        return row.Id;
    }, ct);

    public Task<Guid> UpsertCriterionAsync(Guid adminId, Guid versionId, UpsertInterviewCriterionRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await EditableDraft(versionId, request.ExpectedRevision, ct);
        if (request.Sequence < 1 || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 ||
            request.Description?.Length > 2000 || request.Weight <= 0 || request.MaximumScore <= 0)
            throw Invalid("Invalid interview rubric criterion.");
        if (request.CompetencyId.HasValue) await ValidateCompetency(version, request.CompetencyId.Value, ct);
        var row = request.Id.HasValue ? version.Criteria.SingleOrDefault(x => x.Id == request.Id.Value)
            ?? throw new NotFoundException("Interview criterion not found.") : new AcceleratorInterviewRubricCriterion { InterviewVersionId = version.Id };
        if (!request.Id.HasValue) version.Criteria.Add(row);
        if (version.Criteria.Any(x => x.Id != row.Id && x.Sequence == request.Sequence)) throw new ConflictException("Criterion sequence already exists.");
        row.Sequence = request.Sequence; row.CompetencyId = request.CompetencyId;
        row.Name = request.Name.Trim(); row.Description = request.Description?.Trim() ?? string.Empty;
        row.Weight = request.Weight; row.MaximumScore = request.MaximumScore;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorInterviewRubricCriterion", row.Id, ct);
        return row.Id;
    }, ct);

    public async Task<Guid> ValidateAsync(Guid adminId, Guid versionId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        return (await ValidateForPublication(versionId, ct)).Id;
    }

    public async Task<InterviewAdminVersionResponse> AdminVersionAsync(Guid adminId, Guid versionId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        var version = await store.VersionAsync(versionId, ct) ?? throw new NotFoundException("Interview version not found.");
        return new InterviewAdminVersionResponse(version.Id, version.DefinitionId, version.Version, version.Status,
            version.Revision, version.Competencies.OrderBy(x => x.CompetencyId).Select(x =>
                new InterviewAdminCompetencyResponse(x.Id, x.CompetencyId, x.ExpectedProficiency, x.Weight,
                    x.IsRequired, x.QuestionCoverageTarget)).ToArray(),
            version.Sections.OrderBy(x => x.Sequence).Select(s => new InterviewAdminSectionResponse(s.Id, s.Sequence,
                s.Title, s.Type, s.EstimatedMinutes, s.IsRequired, s.QuestionCount, s.MaxFollowUpsPerQuestion,
                s.Questions.OrderBy(q => q.Sequence).Select(q => new InterviewAdminQuestionResponse(q.Id, s.Id,
                    q.Sequence, q.CompetencyId, q.Kind, q.Type, q.EvaluationMode, q.Difficulty, q.PublicPrompt,
                    q.ExpectedConceptsJson, q.HiddenEvaluatorGuidance, q.DeterministicRulesJson, q.FollowUpPrompt,
                    q.IsActive)).ToArray(), s.AllowStaticFallback)).ToArray(),
            version.Criteria.OrderBy(x => x.Sequence).Select(x => new InterviewAdminCriterionResponse(x.Id, x.Sequence,
                x.CompetencyId, x.Name, x.Description, x.Weight, x.MaximumScore)).ToArray());
    }

    public Task<Guid> PublishAsync(Guid adminId, Guid versionId, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await ValidateForPublication(versionId, ct);
        version.Status = AcceleratorInterviewCatalogStatus.Published;
        version.PublishedAtUtc = Now;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Publish, "AcceleratorInterviewVersion", version.Id, ct);
        return version.Id;
    }, ct);

    public Task<Guid> RetireAsync(Guid adminId, Guid versionId, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await store.VersionAsync(versionId, ct) ?? throw new NotFoundException("Interview version not found.");
        if (version.Status == AcceleratorInterviewCatalogStatus.Retired) return version.Id;
        if (version.Status != AcceleratorInterviewCatalogStatus.Published) throw new ConflictException("Only published interviews can be retired.");
        version.Status = AcceleratorInterviewCatalogStatus.Retired; version.RetiredAtUtc = Now;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorInterviewVersion", version.Id, ct);
        return version.Id;
    }, ct);

    private async Task<AcceleratorInterviewVersion> ValidateForPublication(Guid versionId, CancellationToken ct)
    {
        var version = await store.VersionAsync(versionId, ct) ?? throw new NotFoundException("Interview version not found.");
        if (version.Status != AcceleratorInterviewCatalogStatus.Draft || await store.VersionUsedAsync(versionId, ct))
            throw new ConflictException("Only an unused draft interview can be published.");
        ValidateMetadata(version.Type, version.Difficulty, version.EstimatedMinutes, version.TargetExperienceLevel, version.Instructions);
        if (!version.Definition.IsActive || !version.Sections.Any(x => x.IsRequired) ||
            version.Sections.Select(x => x.Sequence).Distinct().Count() != version.Sections.Count ||
            version.Competencies.Count == 0 || version.Competencies.Sum(x => x.Weight) != 100m ||
            version.Criteria.Count == 0 || version.Criteria.Sum(x => x.Weight) != 100m ||
            version.Criteria.Select(x => x.Sequence).Distinct().Count() != version.Criteria.Count ||
            version.Criteria.Any(x => x.Weight <= 0 || x.MaximumScore <= 0))
            throw Invalid("Interview requires valid sections, competency weights and rubric weights totaling 100.");
        foreach (var mapping in version.Competencies) await ValidateCompetency(version, mapping.CompetencyId, ct);
        foreach (var criterion in version.Criteria.Where(x => x.CompetencyId.HasValue))
            await ValidateCompetency(version, criterion.CompetencyId!.Value, ct);
        var selectedQuestions = version.Sections.SelectMany(section => section.Questions
            .Where(x => x.IsActive && x.Kind == AcceleratorInterviewQuestionKind.Static)
            .OrderBy(x => x.Sequence).ThenBy(x => x.Id).Take(section.QuestionCount)).ToArray();
        var coverageQuestions = selectedQuestions.Concat(version.Sections.SelectMany(section => section.Questions
            .Where(x => x.IsActive && x.Kind == AcceleratorInterviewQuestionKind.AiGenerated))).ToArray();
        if (version.Sections.Sum(x => x.QuestionCount) > 100 || coverageQuestions.Length == 0)
            throw Invalid("Interview question count must be within the session limit.");
        foreach (var mapping in version.Competencies)
        {
            var count = coverageQuestions.Count(x => x.CompetencyId == mapping.CompetencyId);
            if ((mapping.IsRequired && count == 0) ||
                mapping.QuestionCoverageTarget > 0 && 100m * count / coverageQuestions.Length < mapping.QuestionCoverageTarget)
                throw Invalid("Required competency coverage is not met by selected interview questions.");
        }
        foreach (var section in version.Sections)
        {
            var staticQuestions = section.Questions.Where(x => x.IsActive && x.Kind == AcceleratorInterviewQuestionKind.Static).ToArray();
            var aiQuestions = section.Questions.Where(x => x.IsActive && x.Kind == AcceleratorInterviewQuestionKind.AiGenerated).ToArray();
            if (section.QuestionCount is < 1 or > 50 || section.MaxFollowUpsPerQuestion is < 0 or > 3 ||
                (staticQuestions.Length < section.QuestionCount && (aiQuestions.Length == 0 || section.AllowStaticFallback)) ||
                section.Questions.Select(x => x.Sequence).Distinct().Count() != section.Questions.Count)
                throw Invalid("Every interview section requires approved static questions or an explicit AI generation policy.");
            foreach (var question in section.Questions.Where(x => x.IsActive))
            {
                if (string.IsNullOrWhiteSpace(question.PublicPrompt) || !Enum.IsDefined(question.EvaluationMode))
                    throw Invalid("Interview question is incomplete.");
                if (question.Kind == AcceleratorInterviewQuestionKind.AiGenerated &&
                    question.EvaluationMode == AcceleratorInterviewEvaluationMode.Deterministic)
                    throw Invalid("AI-generated questions require authorized subjective evaluation.");
                InterviewScoringPolicy.ValidateConfiguration(question, version.Criteria);
            }
        }
        return version;
    }

    private async Task<AcceleratorInterviewVersion> EditableDraft(Guid id, Guid expectedRevision, CancellationToken ct)
    {
        var version = await store.VersionAsync(id, ct) ?? throw new NotFoundException("Interview version not found.");
        if (version.Status != AcceleratorInterviewCatalogStatus.Draft || await store.VersionUsedAsync(id, ct))
            throw new ConflictException("Only an unused draft interview can be edited.");
        if (expectedRevision == Guid.Empty || version.Revision != expectedRevision)
            throw new ConflictException("Interview draft changed; reload it.");
        return version;
    }

    private async Task ValidateCompetency(AcceleratorInterviewVersion version, Guid competencyId, CancellationToken ct)
    {
        var role = await writes.RoleAsync(version.Definition.TargetRoleId, ct) ?? throw new NotFoundException("Target role not found.");
        var competency = await competencies.CompetencyAsync(competencyId, ct) ?? throw new NotFoundException("Competency not found.");
        if (competency.TaxonomyId != role.TaxonomyId) throw new ConflictException("Interview competency belongs to another taxonomy.");
    }

    private static void ValidateMetadata(AcceleratorInterviewType type, AcceleratorPracticeDifficulty difficulty,
        int minutes, string experience, string instructions)
    {
        if (!Enum.IsDefined(type) || !Enum.IsDefined(difficulty) || minutes is < 1 or > 1000 ||
            string.IsNullOrWhiteSpace(experience) || experience.Length > 100 ||
            string.IsNullOrWhiteSpace(instructions) || instructions.Length > 12000)
            throw Invalid("Invalid interview version metadata.");
    }

    private static void ValidateJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16000) throw Invalid("Interview JSON configuration is invalid.");
        try { using var _ = JsonDocument.Parse(value); }
        catch (JsonException) { throw Invalid("Interview JSON configuration is invalid."); }
    }
    private Task AdminAudit(Guid actor, AuditAction action, string type, Guid id, CancellationToken ct) =>
        audit.AppendAsync(new AuditEvent(action, type, id.ToString(), Actor: new AuditActor(actor, "Administrator")), ct);
    private static BadRequestException Invalid(string message) => new(message, "accelerator_interview_invalid");
}
