using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class AcceleratorPhase4Service(IAcceleratorStore writes, AcceleratorPhase2Service phase2Service,
    IAcceleratorPhase4Store store, AcceleratorService access, IProjectDeterministicEvaluator checker, IAuditWriter audit, TimeProvider clock,
    AcceleratorJourneyWorkService work)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AcceleratorProjectDetailResponse> CatalogDetailAsync(Guid actor, Guid versionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var version = await store.VersionAsync(versionId, ct) ?? throw new NotFoundException("Project version not found.");
        if (version.CatalogStatus != AcceleratorProjectCatalogStatus.Published || !version.IsActive || !version.Definition.IsActive)
            throw new NotFoundException("Project version not found.");
        return DetailDto(version);
    }

    public async Task<AcceleratorProjectEvaluationResponse?> EvaluationAsync(Guid actor, Guid projectId, Guid submissionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        _ = await store.SubmissionAsync(actor, projectId, submissionId, ct) ?? throw new NotFoundException("Project submission not found.");
        var evaluation = await store.EvaluationAsync(actor, submissionId, ct);
        return evaluation is null ? null : EvaluationDto(evaluation);
    }

    public async Task<AcceleratorProjectSubmissionResponse> SubmissionAsync(Guid actor, Guid projectId, Guid submissionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var submission = await store.SubmissionAsync(actor, projectId, submissionId, ct)
            ?? throw new NotFoundException("Project submission not found.");
        return SubmissionDto(submission);
    }

    public async Task<AcceleratorProjectProgressResponse> ProgressAsync(Guid actor, Guid projectId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var project = await store.CandidateProjectAsync(actor, projectId, ct)
            ?? throw new NotFoundException("Candidate project not found.");
        return ProgressDto(project);
    }

    public async Task<AcceleratorProjectDetailResponse> CandidateVersionAsync(Guid actor, Guid projectId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var project = await store.CandidateProjectAsync(actor, projectId, ct)
            ?? throw new NotFoundException("Candidate project not found.");
        var version = await store.VersionAsync(project.ProjectVersionId, ct)
            ?? throw new ConflictException("Historical project version is missing.");
        return DetailDto(version);
    }

    public async Task<AcceleratorProjectMilestoneResponse> CandidateMilestoneAsync(Guid actor, Guid projectId, Guid milestoneId, CancellationToken ct)
    {
        var detail = await CandidateVersionAsync(actor, projectId, ct);
        return detail.Milestones.SingleOrDefault(x => x.Id == milestoneId)
            ?? throw new NotFoundException("Milestone not found in project version.");
    }

    public async Task<AcceleratorProjectAssignmentResponse> CandidateAssignmentAsync(Guid actor, Guid projectId, Guid assignmentId, CancellationToken ct)
    {
        var detail = await CandidateVersionAsync(actor, projectId, ct);
        return detail.Milestones.SelectMany(x => x.Assignments).SingleOrDefault(x => x.Id == assignmentId)
            ?? throw new NotFoundException("Assignment not found in project version.");
    }

    public async Task<AcceleratorProjectProgressResponse> AdminProgressAsync(Guid adminId, Guid candidateId, Guid projectId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        var project = await store.CandidateProjectAsync(candidateId, projectId, ct)
            ?? throw new NotFoundException("Candidate project not found.");
        return ProgressDto(project);
    }

    public async Task<AcceleratorProjectSubmissionResponse> AdminSubmissionAsync(Guid adminId, Guid candidateId,
        Guid projectId, Guid submissionId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        var submission = await store.SubmissionAsync(candidateId, projectId, submissionId, ct)
            ?? throw new NotFoundException("Project submission not found.");
        return SubmissionDto(submission);
    }

    public async Task<AcceleratorProjectEvaluationResponse?> AdminEvaluationAsync(Guid adminId, Guid candidateId,
        Guid projectId, Guid submissionId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        _ = await store.SubmissionAsync(candidateId, projectId, submissionId, ct)
            ?? throw new NotFoundException("Project submission not found.");
        var evaluation = await store.EvaluationAsync(candidateId, submissionId, ct);
        return evaluation is null ? null : EvaluationDto(evaluation);
    }

    public async Task<AcceleratorAdminProjectVersionResponse> AdminVersionAsync(Guid adminId, Guid versionId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        var version = await store.VersionAsync(versionId, ct) ?? throw new NotFoundException("Project version not found.");
        var rubric = await store.ActiveRubricAsync(versionId, ct);
        return new AcceleratorAdminProjectVersionResponse(version.Id, version.DefinitionId, version.Version,
            version.CatalogStatus, version.Revision, version.TargetRoleId, version.Type, version.Difficulty,
            version.EstimatedMinutes, version.Title, version.ShortDescription, version.BusinessContext, version.Instructions,
            version.Competencies.OrderBy(x => x.CompetencyId).Select(x => new AcceleratorAdminProjectCompetencyResponse(
                x.Id, x.CompetencyId, x.IsPrerequisite, x.RequiredProficiency)).ToArray(),
            version.Milestones.OrderBy(x => x.Sequence).Select(x => new AcceleratorAdminProjectMilestoneResponse(
                x.Id, x.Sequence, x.Title, x.Description, x.EstimatedMinutes, x.IsRequired,
                x.Assignments.OrderBy(a => a.Sequence).Select(a => new AcceleratorAdminProjectAssignmentResponse(
                    a.Id, a.Sequence, a.Type, a.EvaluationMode, a.Prompt, a.CompletionRequirements,
                    a.DeterministicRulesJson, a.MaximumAttempts, a.RetryAllowed, a.CooldownMinutes)).ToArray())).ToArray(),
            rubric is null ? null : new AcceleratorAdminProjectRubricResponse(rubric.Id, rubric.Version, rubric.Name,
                rubric.Criteria.OrderBy(x => x.Sequence).Select(x => new AcceleratorAdminProjectCriterionResponse(
                    x.Id, x.Sequence, x.CompetencyId, x.Name, x.Description, x.Weight, x.MaximumScore)).ToArray()));
    }

    public async Task<IReadOnlyList<AcceleratorProjectHistoryEntry>> HistoryAsync(Guid actor, Guid projectId, int page, int pageSize, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        AcceleratorService.Page(page, pageSize);
        _ = await store.CandidateProjectAsync(actor, projectId, ct) ?? throw new NotFoundException("Candidate project not found.");
        var submissions = await store.ProjectSubmissionsAsync(actor, projectId, (page - 1) * pageSize, pageSize, ct);
        var history = new List<AcceleratorProjectHistoryEntry>(submissions.Count);
        foreach (var submission in submissions)
        {
            var evaluation = await store.EvaluationAsync(actor, submission.Id, ct);
            history.Add(new(SubmissionDto(submission), evaluation is null ? null : EvaluationDto(evaluation)));
        }
        return history;
    }

    public Task<Guid> CreateDefinitionAsync(Guid adminId, CreateAcceleratorProjectDefinitionRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var key = request.StableKey?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 120 || key.Any(x => !char.IsAsciiLetterLower(x) && !char.IsAsciiDigit(x) && x != '-'))
            throw Invalid("Project stable key must contain only lowercase letters, digits and hyphens.");
        var existing = await store.DefinitionByStableKeyAsync(key, ct);
        if (existing is not null) return existing.Id;
        var definition = new AcceleratorProjectDefinition { StableKey = key, IsActive = true };
        store.Add(definition);
        await AdminAudit(adminId, AuditAction.Create, "AcceleratorProjectDefinition", definition.Id, ct);
        return definition.Id;
    }, ct);

    public Task<AcceleratorDraftResponse> CreateDraftAsync(Guid adminId, CreateAcceleratorDraftRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        ValidateDraftMetadata(request.TargetRoleId, request.Type, request.Difficulty, request.EstimatedMinutes,
            request.Title, request.ShortDescription, request.BusinessContext, request.Instructions);
        if (request.DefinitionId == Guid.Empty || request.Version < 1) throw Invalid("Project definition and positive version are required.");
        _ = await store.DefinitionAsync(request.DefinitionId, ct) ?? throw new NotFoundException("Project definition not found.");
        _ = await writes.RoleAsync(request.TargetRoleId, ct) ?? throw new NotFoundException("Target role not found.");
        if (await store.VersionByNumberAsync(request.DefinitionId, request.Version, ct) is not null)
            throw new ConflictException("Project version already exists.");
        var version = new AcceleratorProjectVersion { DefinitionId = request.DefinitionId, Version = request.Version,
            TargetRoleId = request.TargetRoleId, Type = request.Type, Difficulty = request.Difficulty,
            EstimatedMinutes = request.EstimatedMinutes, Title = request.Title.Trim(), ShortDescription = request.ShortDescription.Trim(),
            BusinessContext = request.BusinessContext.Trim(), Instructions = request.Instructions.Trim() };
        store.Add(version);
        await AdminAudit(adminId, AuditAction.Create, "AcceleratorProjectVersion", version.Id, ct);
        return new AcceleratorDraftResponse(version.Id, version.Revision);
    }, ct);

    public Task<AcceleratorDraftResponse> EditDraftAsync(Guid adminId, Guid versionId, EditAcceleratorDraftRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        if (version.Revision != request.ExpectedRevision) throw new ConflictException("Draft changed; reload it.");
        ValidateDraftMetadata(request.TargetRoleId, request.Type, request.Difficulty, request.EstimatedMinutes,
            request.Title, request.ShortDescription, request.BusinessContext, request.Instructions);
        _ = await writes.RoleAsync(request.TargetRoleId, ct) ?? throw new NotFoundException("Target role not found.");
        version.TargetRoleId = request.TargetRoleId; version.Type = request.Type; version.Difficulty = request.Difficulty;
        version.EstimatedMinutes = request.EstimatedMinutes; version.Title = request.Title.Trim();
        version.ShortDescription = request.ShortDescription.Trim(); version.BusinessContext = request.BusinessContext.Trim();
        version.Instructions = request.Instructions.Trim(); version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorProjectVersion", version.Id, ct);
        return new AcceleratorDraftResponse(version.Id, version.Revision);
    }, ct);

    public Task<Guid> UpsertCompetencyAsync(Guid adminId, Guid versionId, UpsertAcceleratorProjectCompetencyRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        RequireDraftRevision(version, request.ExpectedRevision);
        if (request.RequiredProficiency is < 0 or > 4) throw Invalid("Required proficiency is outside the supported range.");
        await ValidateCompetencyAsync(version.TargetRoleId, request.CompetencyId, ct);
        var row = version.Competencies.SingleOrDefault(x => x.CompetencyId == request.CompetencyId);
        if (row is null)
        {
            row = new AcceleratorProjectCompetency { ProjectVersionId = version.Id, CompetencyId = request.CompetencyId };
            version.Competencies.Add(row);
        }
        row.IsPrerequisite = request.IsPrerequisite; row.RequiredProficiency = request.RequiredProficiency;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorProjectCompetency", row.Id, ct);
        return row.Id;
    }, ct);

    public Task<Guid> UpsertMilestoneAsync(Guid adminId, Guid versionId, UpsertAcceleratorProjectMilestoneRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        RequireDraftRevision(version, request.ExpectedRevision);
        if (request.Sequence < 1 || request.EstimatedMinutes < 1 || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 || request.Description?.Length > 8000)
            throw Invalid("Invalid project milestone.");
        var milestone = request.Id.HasValue ? version.Milestones.SingleOrDefault(x => x.Id == request.Id.Value)
            ?? throw new NotFoundException("Milestone not found in draft.") : new AcceleratorProjectMilestone { ProjectVersionId = version.Id };
        if (!request.Id.HasValue) version.Milestones.Add(milestone);
        if (version.Milestones.Any(x => x.Id != milestone.Id && x.Sequence == request.Sequence)) throw new ConflictException("Milestone sequence already exists.");
        milestone.Sequence = request.Sequence; milestone.Title = request.Title.Trim(); milestone.Description = request.Description?.Trim() ?? string.Empty;
        milestone.EstimatedMinutes = request.EstimatedMinutes; milestone.IsRequired = request.IsRequired;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorProjectMilestone", milestone.Id, ct);
        return milestone.Id;
    }, ct);

    public Task<Guid> UpsertAssignmentAsync(Guid adminId, Guid versionId, UpsertAcceleratorProjectAssignmentRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        RequireDraftRevision(version, request.ExpectedRevision);
        var milestone = version.Milestones.SingleOrDefault(x => x.Id == request.MilestoneId)
            ?? throw new NotFoundException("Milestone not found in draft.");
        if (request.Sequence < 1 || !Enum.IsDefined(request.Type) || !Enum.IsDefined(request.EvaluationMode) ||
            string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 12000 || request.CompletionRequirements?.Length > 8000 ||
            request.MaximumAttempts is < 1 or > 20 || request.CooldownMinutes is < 0 or > 10080 ||
            string.IsNullOrWhiteSpace(request.DeterministicRulesJson) || request.DeterministicRulesJson.Length > 16000)
            throw Invalid("Invalid project assignment.");
        try { using var _ = JsonDocument.Parse(request.DeterministicRulesJson); }
        catch (JsonException) { throw Invalid("Evaluator rules must be valid JSON."); }
        var assignment = request.Id.HasValue ? milestone.Assignments.SingleOrDefault(x => x.Id == request.Id.Value)
            ?? throw new NotFoundException("Assignment not found in milestone.") : new AcceleratorProjectAssignment { MilestoneId = milestone.Id };
        if (!request.Id.HasValue) milestone.Assignments.Add(assignment);
        if (milestone.Assignments.Any(x => x.Id != assignment.Id && x.Sequence == request.Sequence)) throw new ConflictException("Assignment sequence already exists.");
        assignment.Sequence = request.Sequence; assignment.Type = request.Type; assignment.EvaluationMode = request.EvaluationMode;
        assignment.Prompt = request.Prompt.Trim(); assignment.CompletionRequirements = request.CompletionRequirements?.Trim() ?? string.Empty;
        assignment.DeterministicRulesJson = request.DeterministicRulesJson; assignment.MaximumAttempts = request.MaximumAttempts;
        assignment.RetryAllowed = request.RetryAllowed; assignment.CooldownMinutes = request.CooldownMinutes;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorProjectAssignment", assignment.Id, ct);
        return assignment.Id;
    }, ct);

    public Task<Guid> UpsertRubricAsync(Guid adminId, Guid versionId, UpsertAcceleratorProjectRubricRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        RequireDraftRevision(version, request.ExpectedRevision);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.Version < 1) throw Invalid("Invalid rubric.");
        var rubric = await store.ActiveRubricAsync(versionId, ct);
        if (rubric is null) { rubric = new AcceleratorProjectRubric { ProjectVersionId = versionId, IsActive = true }; store.Add(rubric); }
        rubric.Name = request.Name.Trim(); rubric.Version = request.Version;
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorProjectRubric", rubric.Id, ct);
        return rubric.Id;
    }, ct);

    public Task<Guid> UpsertCriterionAsync(Guid adminId, Guid versionId, UpsertAcceleratorProjectCriterionRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        RequireDraftRevision(version, request.ExpectedRevision);
        var rubric = await store.ActiveRubricAsync(versionId, ct) ?? throw new NotFoundException("Draft rubric not found.");
        if (request.Sequence < 1 || request.Weight <= 0 || request.MaximumScore <= 0 || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Length > 120 || request.Description?.Length > 2000) throw Invalid("Invalid rubric criterion.");
        if (request.CompetencyId.HasValue) await ValidateCompetencyAsync(version.TargetRoleId, request.CompetencyId.Value, ct);
        var criterion = request.Id.HasValue ? rubric.Criteria.SingleOrDefault(x => x.Id == request.Id.Value)
            ?? throw new NotFoundException("Criterion not found in rubric.") : new AcceleratorProjectRubricCriterion { RubricId = rubric.Id };
        if (!request.Id.HasValue) rubric.Criteria.Add(criterion);
        if (rubric.Criteria.Any(x => x.Id != criterion.Id && x.Sequence == request.Sequence)) throw new ConflictException("Criterion sequence already exists.");
        criterion.Sequence = request.Sequence; criterion.CompetencyId = request.CompetencyId; criterion.Name = request.Name.Trim();
        criterion.Description = request.Description?.Trim() ?? string.Empty; criterion.Weight = request.Weight;
        criterion.MaximumScore = request.MaximumScore; version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Update, "AcceleratorProjectRubricCriterion", criterion.Id, ct);
        return criterion.Id;
    }, ct);

    public Task<Guid> RemoveCompetencyAsync(Guid adminId, Guid versionId, Guid competencyId, Guid expectedRevision, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        RequireDraftRevision(version, expectedRevision);
        var row = version.Competencies.SingleOrDefault(x => x.CompetencyId == competencyId)
            ?? throw new NotFoundException("Project competency not found in draft.");
        var rubric = await store.ActiveRubricAsync(versionId, ct);
        if (rubric?.Criteria.Any(x => x.CompetencyId == competencyId) == true)
            throw new ConflictException("Remove mapped rubric criteria before removing this competency.");
        store.Remove(row);
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Delete, "AcceleratorProjectCompetency", row.Id, ct);
        return row.Id;
    }, ct);

    public Task<Guid> RemoveMilestoneAsync(Guid adminId, Guid versionId, Guid milestoneId, Guid expectedRevision, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        RequireDraftRevision(version, expectedRevision);
        var milestone = version.Milestones.SingleOrDefault(x => x.Id == milestoneId)
            ?? throw new NotFoundException("Project milestone not found in draft.");
        foreach (var assignment in milestone.Assignments.ToArray()) store.Remove(assignment);
        store.Remove(milestone);
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Delete, "AcceleratorProjectMilestone", milestone.Id, ct);
        return milestone.Id;
    }, ct);

    public Task<Guid> RemoveAssignmentAsync(Guid adminId, Guid versionId, Guid assignmentId, Guid expectedRevision, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        RequireDraftRevision(version, expectedRevision);
        var assignment = version.Milestones.SelectMany(x => x.Assignments).SingleOrDefault(x => x.Id == assignmentId)
            ?? throw new NotFoundException("Project assignment not found in draft.");
        store.Remove(assignment);
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Delete, "AcceleratorProjectAssignment", assignment.Id, ct);
        return assignment.Id;
    }, ct);

    public Task<Guid> RemoveCriterionAsync(Guid adminId, Guid versionId, Guid criterionId, Guid expectedRevision, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await RequiredEditableDraftAsync(versionId, ct);
        RequireDraftRevision(version, expectedRevision);
        var rubric = await store.ActiveRubricAsync(versionId, ct) ?? throw new NotFoundException("Draft rubric not found.");
        var criterion = rubric.Criteria.SingleOrDefault(x => x.Id == criterionId)
            ?? throw new NotFoundException("Rubric criterion not found in draft.");
        store.Remove(criterion);
        version.Revision = Guid.NewGuid();
        await AdminAudit(adminId, AuditAction.Delete, "AcceleratorProjectRubricCriterion", criterion.Id, ct);
        return criterion.Id;
    }, ct);

    public Task<Guid> PublishVersionAsync(Guid adminId, Guid versionId, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await ValidateForPublicationAsync(versionId, ct);
        version.CatalogStatus = AcceleratorProjectCatalogStatus.Published;
        version.IsActive = true;
        await audit.AppendAsync(new AuditEvent(AuditAction.Publish, "AcceleratorProjectVersion", version.Id.ToString(), Actor: new AuditActor(adminId, "Administrator")), ct);
        return version.Id;
    }, ct);

    public async Task<Guid> ValidateVersionAsync(Guid adminId, Guid versionId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        return (await ValidateForPublicationAsync(versionId, ct)).Id;
    }

    private async Task<AcceleratorProjectVersion> ValidateForPublicationAsync(Guid versionId, CancellationToken ct)
    {
        var version = await RequiredEditableDraftAsync(versionId, ct);
        if (!version.Definition.IsActive || !Enum.IsDefined(version.Type) || !Enum.IsDefined(version.Difficulty) ||
            version.TargetRoleId == Guid.Empty || string.IsNullOrWhiteSpace(version.Title) || string.IsNullOrWhiteSpace(version.ShortDescription) ||
            string.IsNullOrWhiteSpace(version.BusinessContext) || string.IsNullOrWhiteSpace(version.Instructions) ||
            version.EstimatedMinutes <= 0 || !version.Milestones.Any(x => x.IsRequired) ||
            !version.Competencies.Any(x => !x.IsPrerequisite) ||
            version.Competencies.Any(x => x.CompetencyId == Guid.Empty || x.RequiredProficiency is < 0 or > 4) ||
            version.Milestones.Any(x => x.Sequence < 1 || string.IsNullOrWhiteSpace(x.Title) || x.EstimatedMinutes <= 0 ||
                x.IsRequired && x.Assignments.Count == 0 || x.Assignments.Any(a => a.Sequence < 1 ||
                    string.IsNullOrWhiteSpace(a.Prompt) || a.MaximumAttempts is < 1 or > 20 || a.CooldownMinutes is < 0 or > 10080 || a.MilestoneId != x.Id) ||
                x.Assignments.Select(a => a.Sequence).Distinct().Count() != x.Assignments.Count) ||
            version.Milestones.Select(x => x.Sequence).Distinct().Count() != version.Milestones.Count)
            throw Invalid("Project version requires a complete brief, ordered milestones and assignments.");
        _ = await writes.RoleAsync(version.TargetRoleId, ct) ?? throw new NotFoundException("Target role not found.");
        foreach (var competency in version.Competencies) await ValidateCompetencyAsync(version.TargetRoleId, competency.CompetencyId, ct);
        var rubric = await store.ActiveRubricAsync(version.Id, ct) ?? throw Invalid("Project version requires an active rubric.");
        ValidateRubric(rubric);
        foreach (var criterion in rubric.Criteria.Where(x => x.CompetencyId.HasValue))
        {
            if (!version.Competencies.Any(x => x.CompetencyId == criterion.CompetencyId))
                throw Invalid("Rubric criterion competency must be mapped to the project version.");
            await ValidateCompetencyAsync(version.TargetRoleId, criterion.CompetencyId!.Value, ct);
        }
        foreach (var assignment in version.Milestones.SelectMany(x => x.Assignments))
            checker.ValidateConfiguration(assignment, rubric);
        return version;
    }

    public Task<Guid> RetireVersionAsync(Guid adminId, Guid versionId, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var version = await store.VersionAsync(versionId, ct) ?? throw new NotFoundException("Project version not found.");
        if (version.CatalogStatus == AcceleratorProjectCatalogStatus.Retired) return version.Id;
        if (version.CatalogStatus != AcceleratorProjectCatalogStatus.Published) throw new ConflictException("Only a published project version can be retired.");
        version.CatalogStatus = AcceleratorProjectCatalogStatus.Retired;
        version.IsActive = false;
        await audit.AppendAsync(new AuditEvent(AuditAction.Update, "AcceleratorProjectVersion", version.Id.ToString(), Actor: new AuditActor(adminId, "Administrator")), ct);
        return version.Id;
    }, ct);

    public async Task<IReadOnlyList<AcceleratorProjectRecommendation>> RecommendAsync(Guid actor, Guid goalId, CancellationToken ct, Guid? enrollmentId = null)
    {
        await access.Candidate(actor, ct);
        var scope = await work.ResolveAsync(actor, goalId, enrollmentId, ct);
        var versions = (await store.ActiveVersionsAsync(scope.RoleId, 100, ct, scope.Requirements.Select(x=>x.CompetencyId).ToArray())).Where(v => AcceleratorJourneyWorkService.Compatible(scope, v.TargetRoleId, v.Competencies.Select(x => x.CompetencyId))).ToArray();
        var evidence = scope.Evidence;
        var existing = await store.CandidateProjectsAsync(actor, 0, 100, ct);
        var roadmap = scope.Roadmap;
        var proficiency = evidence.GroupBy(x => x.CompetencyId).ToDictionary(x => x.Key, x => x.OrderByDescending(e => e.Confidence).ThenByDescending(e => e.EvaluatedAtUtc).First().Proficiency);
        var completedVersions = existing.Where(x => x.JourneyContextId == scope.Context.Id && x.Status == AcceleratorCandidateProjectStatus.Completed).Select(x => x.ProjectVersionId).ToHashSet();
        return versions.Where(v => !completedVersions.Contains(v.Id) &&
                v.Competencies.Where(x => x.IsPrerequisite).All(x => (int)proficiency.GetValueOrDefault(x.CompetencyId) >= x.RequiredProficiency))
            .Select(v => new { Version = v, Priority = v.Competencies.Count(x => !x.IsPrerequisite && roadmap?.Nodes.Any(n => n.CompetencyId == x.CompetencyId && n.Status != AcceleratorNodeStatus.Completed) == true) })
            .OrderByDescending(x => x.Priority).ThenBy(x => x.Version.Difficulty).ThenBy(x => x.Version.Definition.StableKey).ThenByDescending(x => x.Version.Version)
            .Select(x => new AcceleratorProjectRecommendation(x.Version.Id, x.Version.Title, x.Version.Difficulty,
                x.Priority > 0 ? $"Addresses {x.Priority} current roadmap skill gaps." : "Matches the target role and verified prerequisites."))
            .ToArray();
    }

    public async Task<CandidateAcceleratorProjectResponse> StartProjectAsync(Guid actor, StartAcceleratorProjectRequest request, CancellationToken ct)
    {
        var resolved = await work.ResolveAsync(actor, request.GoalId, request.EnrollmentId, ct);
        return await writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        if (request.RequestKey == Guid.Empty || request.ProjectVersionId == Guid.Empty) throw Invalid("Project start identifiers are required.");
        var hash = request.EnrollmentId.HasValue ? Hash(request) : Hash(new { request.RequestKey, request.GoalId, request.ProjectVersionId });
        var previous = await store.CandidateProjectByKeyAsync(actor, request.RequestKey, ct);
        if (previous is not null)
        {
            if (previous.RequestHash != hash) throw new ConflictException("Project start key was used for a different request.");
            return ProjectDto(previous);
        }
        var scope = await work.OwnedAsync(actor, resolved.Context.Id, ct);
        var version = await store.VersionAsync(request.ProjectVersionId, ct) ?? throw new NotFoundException("Project version not found.");
        if (!version.IsActive || version.CatalogStatus != AcceleratorProjectCatalogStatus.Published || !version.Definition.IsActive ||
            !AcceleratorJourneyWorkService.Compatible(scope, version.TargetRoleId, version.Competencies.Select(x => x.CompetencyId))) throw new ConflictException("Project version is unavailable for this journey.");
        var evidence = scope.Evidence;
        foreach (var prerequisite in version.Competencies.Where(x => x.IsPrerequisite))
            if (!evidence.Any(x => x.CompetencyId == prerequisite.CompetencyId && (int)x.Proficiency >= prerequisite.RequiredProficiency))
                throw new ConflictException("Project prerequisites are not yet demonstrated.", "accelerator_project_prerequisite");
        var first = version.Milestones.Where(x => x.IsRequired).OrderBy(x => x.Sequence).FirstOrDefault()
            ?? throw new ConflictException("Project version has no required milestones.");
        var project = new AcceleratorCandidateProject { UserId = actor, GoalId = scope.Context.CareerGoalId, JourneyContextId = scope.Context.Id, ProjectVersionId = version.Id,
            RequestKey = request.RequestKey, RequestHash = hash, StartedAtUtc = Now, Status = AcceleratorCandidateProjectStatus.InProgress, CurrentMilestoneId = first.Id };
        store.Add(project); await Audit(actor, AuditAction.Create, "AcceleratorCandidateProject", project.Id, ct);
        return ProjectDto(project);
    }, ct);
    }

    public async Task<CandidateAcceleratorProjectResponse> ProjectAsync(Guid actor, Guid id, CancellationToken ct)
    { await access.Candidate(actor, ct); return ProjectDto(await store.CandidateProjectAsync(actor, id, ct) ?? throw new NotFoundException("Candidate project not found.")); }

    public async Task<IReadOnlyList<CandidateAcceleratorProjectResponse>> ProjectsAsync(Guid actor, int page, int pageSize, CancellationToken ct, Guid? contextId = null)
    {
        await access.Candidate(actor, ct); AcceleratorService.Page(page, pageSize);
        if (contextId.HasValue) await work.OwnedAsync(actor,contextId.Value,ct);
        return (await store.CandidateProjectsAsync(actor,(page-1)*pageSize,pageSize,ct,contextId)).Select(ProjectDto).ToArray();
    }

    public Task<CandidateAcceleratorProjectResponse> ChangeStatusAsync(Guid actor, Guid projectId,
        ChangeAcceleratorProjectStatusRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        var project = await store.CandidateProjectAsync(actor, projectId, ct) ?? throw new NotFoundException("Candidate project not found.");
        if (project.Revision != request.ExpectedRevision) throw new ConflictException("Candidate project changed; reload it.");
        var allowed = project.Status switch
        {
            AcceleratorCandidateProjectStatus.InProgress => request.Status == AcceleratorCandidateProjectStatus.Abandoned,
            AcceleratorCandidateProjectStatus.NeedsRevision => request.Status == AcceleratorCandidateProjectStatus.InProgress,
            _ => false
        };
        if (!allowed) throw new ConflictException("Invalid candidate project state transition.");
        project.Status = request.Status;
        project.Revision = Guid.NewGuid();
        await Audit(actor, AuditAction.Update, "AcceleratorCandidateProject", project.Id, ct);
        return ProjectDto(project);
    }, ct);

    public Task<AcceleratorProjectSubmissionResponse> SubmitAsync(Guid actor, Guid projectId, SubmitAcceleratorAssignmentRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        if (request.RequestKey == Guid.Empty || request.AssignmentId == Guid.Empty || string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 32000) throw Invalid("Invalid assignment submission.");
        var project = await store.CandidateProjectAsync(actor, projectId, ct) ?? throw new NotFoundException("Candidate project not found.");
        var hash = Hash(request.Content);
        var previous = await store.SubmissionByKeyAsync(actor, projectId, request.RequestKey, ct);
        if (previous is not null)
        {
            if (previous.AssignmentId != request.AssignmentId || previous.ContentHash != hash) throw new ConflictException("Submission key was used for different content.");
            return SubmissionDto(previous);
        }
        if (project.Revision != request.ExpectedRevision) throw new ConflictException("Candidate project changed; reload it.");
        if (project.Status is not (AcceleratorCandidateProjectStatus.InProgress or AcceleratorCandidateProjectStatus.NeedsRevision)) throw new ConflictException("Project is not accepting submissions.");
        var assignment = await store.AssignmentAsync(project.ProjectVersionId, request.AssignmentId, ct) ?? throw new NotFoundException("Assignment not found in this project version.");
        if (assignment.Milestone.IsRequired && project.CurrentMilestoneId != assignment.MilestoneId)
            throw new ConflictException("Complete the current milestone first.");
        if (project.RequiredRevisionAssignmentId.HasValue && project.RequiredRevisionAssignmentId != assignment.Id)
            throw new ConflictException("Revise the failed required assignment before starting another.");
        var history = await store.SubmissionsAsync(actor, projectId, assignment.Id, ct);
        if (history.Count >= assignment.MaximumAttempts || history.Count > 0 && !assignment.RetryAllowed) throw new ConflictException("Assignment retry limit reached.");
        if (history.Count > 0 && history[^1].SubmittedAtUtc.AddMinutes(assignment.CooldownMinutes) > Now) throw new ConflictException("Assignment retry cooldown is active.");
        var submission = new AcceleratorProjectSubmission { CandidateProjectId = project.Id, AssignmentId = assignment.Id,
            AttemptNumber = history.Count + 1, RequestKey = request.RequestKey, Content = request.Content, ContentHash = hash,
            Status = AcceleratorSubmissionStatus.Submitted, SubmittedAtUtc = Now };
        store.Add(submission); project.Status = AcceleratorCandidateProjectStatus.Submitted; project.Revision = Guid.NewGuid();
        await Audit(actor, AuditAction.Create, "AcceleratorProjectSubmission", submission.Id, ct); return SubmissionDto(submission);
    }, ct);

    // Explicit administrator assessment; supplied observations are never labeled as automated verification.
    public async Task<AcceleratorProjectEvaluationResponse> EvaluateManualAsync(Guid actor, Guid candidateId, Guid projectId, Guid submissionId,
        IReadOnlyList<AcceleratorCriterionObservation> observations, CancellationToken ct)
    {
        await access.Administrator(actor, ct);
        await BeginEvaluationAsync(actor, candidateId, projectId, submissionId, true, ct);
        return await writes.WriteAsync(async () =>
        {
        var project = await store.CandidateProjectAsync(candidateId, projectId, ct);
        if (project is null) throw new NotFoundException("Candidate project not found.");
        var submission = await store.SubmissionAsync(candidateId, projectId, submissionId, ct);
        if (submission is null) throw new NotFoundException("Project submission not found.");
        var existing = await store.EvaluationAsync(candidateId, submissionId, ct);
        if (existing is not null) return EvaluationDto(existing);
        var rubric = await store.ActiveRubricAsync(project.ProjectVersionId, ct) ?? throw new ConflictException("No active project rubric exists.");
        ValidateRubric(rubric);
        var criteria = rubric.Criteria.OrderBy(x => x.Sequence).ToArray();
        if (observations.Count != criteria.Length || observations.Select(x => x.CriterionId).Distinct().Count() != criteria.Length)
            throw Invalid("Objective observations must match every rubric criterion exactly once.");
        var evaluation = new AcceleratorProjectEvaluation { SubmissionId = submission.Id, MaximumScore = 100,
            Evaluator = AcceleratorProjectEvaluator.Manual, Confidence = 1, EvaluatedAtUtc = Now,
            Feedback = "Administrator evaluated the weighted rubric criteria." };
        foreach (var criterion in criteria)
        {
            var observation = observations.SingleOrDefault(x => x.CriterionId == criterion.Id);
            if (observation is null || observation.Score < 0 || observation.Score > criterion.MaximumScore)
                throw Invalid("Objective criterion score is outside its allowed range.");
            evaluation.Scores.Add(new AcceleratorProjectEvaluationScore { EvaluationId = evaluation.Id, CriterionId = criterion.Id,
                Score = observation.Score, Feedback = string.Empty });
            evaluation.Score += criterion.Weight * observation.Score / criterion.MaximumScore;
        }
        evaluation.Score = decimal.Round(evaluation.Score, 2);
        store.Add(evaluation);
        await phase2Service.RecordProjectEvaluationAsync(project, submission, evaluation, rubric, ct);
        await UpdateProgressAsync(project, submission, evaluation.Score, ct);
        await audit.AppendAsync(new AuditEvent(AuditAction.Confirm, "AcceleratorProjectEvaluation", evaluation.Id.ToString(), Actor: new AuditActor(actor, "Administrator")), ct);
        return EvaluationDto(evaluation);
        }, ct);
    }

    public async Task<AcceleratorProjectEvaluationResponse> EvaluateDeterministicAsync(Guid actor, Guid projectId, Guid submissionId,
        CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        await BeginEvaluationAsync(actor, actor, projectId, submissionId, false, ct);
        return await writes.WriteAsync(async () =>
        {
        var project = await store.CandidateProjectAsync(actor, projectId, ct) ?? throw new NotFoundException("Candidate project not found.");
        var submission = await store.SubmissionAsync(actor, projectId, submissionId, ct) ?? throw new NotFoundException("Project submission not found.");
        var prior = await store.EvaluationAsync(actor, submissionId, ct);
        if (prior is not null) return EvaluationDto(prior);
        var assignment = await store.AssignmentAsync(project.ProjectVersionId, submission.AssignmentId, ct)
            ?? throw new ConflictException("Assignment no longer belongs to this project version.");
        var rubric = await store.ActiveRubricAsync(project.ProjectVersionId, ct) ?? throw new ConflictException("Published rubric is missing.");
        var result = checker.Evaluate(assignment, rubric, submission.Content);
        if (!result.Supported) throw new ConflictException("This assignment requires manual or authorized AI evaluation.", "accelerator_project_manual_evaluation_required");
        var evaluation = new AcceleratorProjectEvaluation { SubmissionId = submission.Id, MaximumScore = 100,
            Evaluator = AcceleratorProjectEvaluator.Deterministic, Confidence = 1, EvaluatedAtUtc = Now,
            Feedback = "Validated against published deterministic criteria." };
        foreach (var criterion in rubric.Criteria)
        {
            var observation = result.Observations.Single(x => x.CriterionId == criterion.Id);
            evaluation.Score += criterion.Weight * observation.Score / criterion.MaximumScore;
            evaluation.Scores.Add(new AcceleratorProjectEvaluationScore { EvaluationId = evaluation.Id, CriterionId = criterion.Id,
                Score = observation.Score, Feedback = string.Empty });
        }
        evaluation.Score = decimal.Round(evaluation.Score, 2);
        store.Add(evaluation);
        await phase2Service.RecordProjectEvaluationAsync(project, submission, evaluation, rubric, ct);
        await UpdateProgressAsync(project, submission, evaluation.Score, ct);
        await Audit(actor, AuditAction.Confirm, "AcceleratorProjectEvaluation", evaluation.Id, ct);
        return EvaluationDto(evaluation);
        }, ct);
    }

    // Trusted provider/reconciliation boundary. The candidate cannot supply rubric observations here.
    public Task<AcceleratorProjectEvaluationResponse> RecordVerifiedAiAsync(Guid candidateId, Guid projectId,
        Guid submissionId, string structuredJson, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var project = await store.CandidateProjectAsync(candidateId, projectId, ct)
            ?? throw new NotFoundException("Candidate project not found.");
        var submission = await store.SubmissionAsync(candidateId, projectId, submissionId, ct)
            ?? throw new NotFoundException("Project submission not found.");
        var prior = await store.EvaluationAsync(candidateId, submissionId, ct);
        if (prior is not null) return EvaluationDto(prior);
        var assignment = await store.AssignmentAsync(project.ProjectVersionId, submission.AssignmentId, ct)
            ?? throw new ConflictException("Assignment is missing from the published version.");
        if (assignment.EvaluationMode != AcceleratorProjectEvaluationMode.Ai)
            throw new ConflictException("Assignment does not permit AI evaluation.");
        var rubric = await store.ActiveRubricAsync(project.ProjectVersionId, ct)
            ?? throw new ConflictException("Published rubric is missing.");
        ValidateRubric(rubric);
        var observations = ProjectAiResultParser.Parse(structuredJson, rubric);
        var evaluation = new AcceleratorProjectEvaluation { SubmissionId = submission.Id, MaximumScore = 100,
            Evaluator = AcceleratorProjectEvaluator.ProviderAssisted, Confidence = .6m, EvaluatedAtUtc = Now,
            Feedback = "Verified provider evaluation of published rubric criteria." };
        foreach (var criterion in rubric.Criteria)
        {
            var observation = observations.Single(x => x.CriterionId == criterion.Id);
            evaluation.Score += criterion.Weight * observation.Score / criterion.MaximumScore;
            evaluation.Scores.Add(new AcceleratorProjectEvaluationScore { EvaluationId = evaluation.Id,
                CriterionId = criterion.Id, Score = observation.Score, Feedback = string.Empty });
        }
        evaluation.Score = decimal.Round(evaluation.Score, 2);
        store.Add(evaluation);
        await phase2Service.RecordProjectEvaluationAsync(project, submission, evaluation, rubric, ct);
        await UpdateProgressAsync(project, submission, evaluation.Score, ct);
        await Audit(candidateId, AuditAction.Confirm, "AcceleratorProjectEvaluation", evaluation.Id, ct);
        return EvaluationDto(evaluation);
    }, ct);

    public Task<int> ResolveVerifiedProviderFailureAsync(Guid candidateId, Guid projectId, Guid submissionId,
        CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var project = await store.CandidateProjectAsync(candidateId, projectId, ct)
            ?? throw new NotFoundException("Candidate project not found.");
        var submission = await store.SubmissionAsync(candidateId, projectId, submissionId, ct)
            ?? throw new NotFoundException("Project submission not found.");
        if (await store.EvaluationAsync(candidateId, submissionId, ct) is not null) return 0;
        if (project.Status is not (AcceleratorCandidateProjectStatus.Submitted or AcceleratorCandidateProjectStatus.UnderEvaluation))
            return 0;
        var assignment = await store.AssignmentAsync(project.ProjectVersionId, submission.AssignmentId, ct)
            ?? throw new ConflictException("Project assignment is missing.");
        project.Status = assignment.Milestone.IsRequired ? AcceleratorCandidateProjectStatus.NeedsRevision
            : AcceleratorCandidateProjectStatus.InProgress;
        project.RequiredRevisionAssignmentId = assignment.Milestone.IsRequired ? assignment.Id : null;
        project.Revision = Guid.NewGuid();
        await Audit(candidateId, AuditAction.Update, "AcceleratorCandidateProject", project.Id, ct);
        return 1;
    }, ct);

    private Task<int> BeginEvaluationAsync(Guid actor, Guid candidateId, Guid projectId, Guid submissionId,
        bool administrator, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var project = await store.CandidateProjectAsync(candidateId, projectId, ct)
            ?? throw new NotFoundException("Candidate project not found.");
        var submission = await store.SubmissionAsync(candidateId, projectId, submissionId, ct)
            ?? throw new NotFoundException("Project submission not found.");
        if (await store.EvaluationAsync(candidateId, submission.Id, ct) is not null) return 0;
        if (!administrator)
        {
            var assignment = await store.AssignmentAsync(project.ProjectVersionId, submission.AssignmentId, ct)
                ?? throw new ConflictException("Assignment no longer belongs to this project version.");
            if (assignment.EvaluationMode != AcceleratorProjectEvaluationMode.Deterministic)
                throw new ConflictException("This assignment requires manual or authorized AI evaluation.", "accelerator_project_manual_evaluation_required");
        }
        if (project.Status == AcceleratorCandidateProjectStatus.UnderEvaluation) return 0;
        if (project.Status != AcceleratorCandidateProjectStatus.Submitted)
            throw new ConflictException("Project is not awaiting evaluation.");
        project.Status = AcceleratorCandidateProjectStatus.UnderEvaluation;
        project.Revision = Guid.NewGuid();
        await audit.AppendAsync(new AuditEvent(AuditAction.Update, "AcceleratorCandidateProject", project.Id.ToString(),
            Actor: new AuditActor(actor, administrator ? "Administrator" : "Candidate")), ct);
        return 1;
    }, ct);

    public static void ValidateRubric(AcceleratorProjectRubric rubric)
    {
        var criteria = rubric.Criteria.ToArray();
        if (criteria.Length == 0 || criteria.Select(x => x.Sequence).Distinct().Count() != criteria.Length ||
            criteria.Any(x => x.Sequence < 1 || x.Weight <= 0 || x.MaximumScore <= 0) ||
            criteria.Sum(x => x.Weight) != 100m)
            throw Invalid("A published rubric requires unique positive criteria with weights totaling 100.");
    }

    private async Task<AcceleratorProjectVersion> RequiredEditableDraftAsync(Guid versionId, CancellationToken ct)
    {
        var version = await store.VersionAsync(versionId, ct) ?? throw new NotFoundException("Project version not found.");
        if (version.CatalogStatus != AcceleratorProjectCatalogStatus.Draft || await store.VersionUsedAsync(version.Id, ct))
            throw new ConflictException("Only an unreferenced draft project version can be edited.");
        return version;
    }

    private async Task ValidateCompetencyAsync(Guid roleId, Guid competencyId, CancellationToken ct)
    {
        var role = await writes.RoleAsync(roleId, ct) ?? throw new NotFoundException("Target role not found.");
        var competency = await store.CompetencyAsync(competencyId, ct) ?? throw new NotFoundException("Competency not found.");
        if (competency.TaxonomyId != role.TaxonomyId) throw new ConflictException("Competency belongs to another taxonomy.");
    }

    private static void ValidateDraftMetadata(Guid roleId, AcceleratorProjectType type, AcceleratorPracticeDifficulty difficulty,
        int minutes, string title, string description, string businessContext, string instructions)
    {
        if (roleId == Guid.Empty || !Enum.IsDefined(type) || !Enum.IsDefined(difficulty) || minutes is < 1 or > 100000 ||
            string.IsNullOrWhiteSpace(title) || title.Length > 200 || string.IsNullOrWhiteSpace(description) || description.Length > 2000 ||
            string.IsNullOrWhiteSpace(businessContext) || businessContext.Length > 12000 ||
            string.IsNullOrWhiteSpace(instructions) || instructions.Length > 12000)
            throw Invalid("Invalid project draft metadata.");
    }

    private static void RequireDraftRevision(AcceleratorProjectVersion version, Guid expectedRevision)
    {
        if (expectedRevision == Guid.Empty || version.Revision != expectedRevision)
            throw new ConflictException("Draft changed; reload it.");
    }

    private Task AdminAudit(Guid actor, AuditAction action, string type, Guid id, CancellationToken ct) =>
        audit.AppendAsync(new AuditEvent(action, type, id.ToString(), Actor: new AuditActor(actor, "Administrator")), ct);

    private async Task UpdateProgressAsync(AcceleratorCandidateProject project, AcceleratorProjectSubmission submission, decimal score, CancellationToken ct)
    {
        var version = await store.VersionAsync(project.ProjectVersionId, ct)
            ?? throw new ConflictException("Project version is missing.");
        if (version.Milestones.SelectMany(x => x.Assignments).All(x => x.Id != submission.AssignmentId))
            throw new ConflictException("Project assignment is missing.");
        if (version.Milestones.Where(x => !x.IsRequired).SelectMany(x => x.Assignments).Any(x => x.Id == submission.AssignmentId))
        {
            project.Status = AcceleratorCandidateProjectStatus.InProgress;
            project.Revision = Guid.NewGuid();
            return;
        }

        var results = await store.EvaluatedAssignmentsAsync(project.UserId, project.Id, ct);
        var outcome = ProjectCompletionPolicy.Calculate(version,
            results.Append(new(submission.AssignmentId, submission.AttemptNumber, score)));
        project.ProgressPercent = outcome.ProgressPercent;
        if (outcome.NextMilestoneId is null)
        {
            project.Status = AcceleratorCandidateProjectStatus.Completed;
            project.CurrentMilestoneId = null;
            project.RequiredRevisionAssignmentId = null;
            project.CompletedAtUtc = Now;
            project.FinalScore = outcome.FinalScore;
        }
        else if (outcome.NextMilestoneId == project.CurrentMilestoneId && score < ProjectCompletionPolicy.AcceptancePercent)
        {
            project.Status = AcceleratorCandidateProjectStatus.NeedsRevision;
            project.RequiredRevisionAssignmentId = submission.AssignmentId;
        }
        else
        {
            project.Status = AcceleratorCandidateProjectStatus.InProgress;
            project.CurrentMilestoneId = outcome.NextMilestoneId;
            project.RequiredRevisionAssignmentId = null;
        }
        project.Revision = Guid.NewGuid();
    }

    private static AcceleratorProjectEvaluationResponse EvaluationDto(AcceleratorProjectEvaluation evaluation) =>
        new(evaluation.Id, evaluation.SubmissionId, evaluation.Score, evaluation.MaximumScore, evaluation.Feedback, evaluation.EvaluatedAtUtc);

    private static AcceleratorProjectDetailResponse DetailDto(AcceleratorProjectVersion version) =>
        new(version.Id, version.Title, version.ShortDescription, version.BusinessContext, version.Instructions,
            version.Milestones.OrderBy(x => x.Sequence).Select(x => new AcceleratorProjectMilestoneResponse(x.Id, x.Sequence, x.Title, x.Description,
                x.Assignments.OrderBy(a => a.Sequence).Select(a => new AcceleratorProjectAssignmentResponse(a.Id, a.Prompt, a.Type, a.MaximumAttempts)).ToArray())).ToArray());

    private static AcceleratorProjectProgressResponse ProgressDto(AcceleratorCandidateProject project) =>
        new(project.Id, project.Status, project.ProgressPercent, project.CurrentMilestoneId,
            project.FinalScore, project.CompletedAtUtc, project.Revision);

    private static CandidateAcceleratorProjectResponse ProjectDto(AcceleratorCandidateProject project) =>
        new(project.Id, project.ProjectVersionId, project.Status, project.ProgressPercent, project.Revision, project.JourneyContextId, project.GoalId);
    private static AcceleratorProjectSubmissionResponse SubmissionDto(AcceleratorProjectSubmission submission) =>
        new(submission.Id, submission.AssignmentId, submission.AttemptNumber, submission.Status, submission.SubmittedAtUtc);
    private Task Audit(Guid actor, AuditAction action, string type, Guid id, CancellationToken ct) =>
        audit.AppendAsync(new AuditEvent(action, type, id.ToString(), Actor: new AuditActor(actor, "Candidate")), ct);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static BadRequestException Invalid(string message) => new(message, "accelerator_invalid_request");
}
