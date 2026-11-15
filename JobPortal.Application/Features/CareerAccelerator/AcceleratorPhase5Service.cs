using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class AcceleratorPhase5Service(IAcceleratorStore writes, IAcceleratorPhase2Store phase2,
    IAcceleratorPhase5Store store, IAcceleratorPhase3Store practice, IAcceleratorPhase4Store projects,
    AcceleratorService access, IAuditWriter audit, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<InterviewRecommendationResponse>> RecommendationsAsync(Guid actor, Guid goalId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var goal = await Goal(actor, goalId, ct);
        var versions = await store.PublishedVersionsAsync(goal.RoleId, 50, ct);
        var evidence = await phase2.EvidenceAsync(actor, goalId, ct);
        var history = await store.SessionsAsync(actor, goalId, 0, 100, ct);
        var completedProjects = (await projects.CandidateProjectsAsync(actor, 0, 100, ct))
            .Count(x => x.GoalId == goalId && x.FinalScore.HasValue);
        var names = (await store.CompetenciesAsync(versions.SelectMany(x => x.Competencies)
            .Select(x => x.CompetencyId).Distinct().ToArray(), ct)).ToDictionary(x => x.Id);
        return versions.Where(version => !history.Any(x => x.InterviewVersionId == version.Id &&
            x.Status == AcceleratorInterviewSessionStatus.Completed && x.OverallScore >= 85m &&
            x.CompletedAtUtc >= Now.AddDays(-30))).Select(version =>
        {
            var weak = version.Competencies.Where(mapping => !evidence.Any(item => item.CompetencyId == mapping.CompetencyId &&
                item.Proficiency >= (AcceleratorProficiency)mapping.ExpectedProficiency && item.Confidence >= .6m))
                .OrderByDescending(x => x.Weight).ToArray();
            var prior = history.Where(x => x.InterviewVersionId == version.Id && x.Status == AcceleratorInterviewSessionStatus.Completed)
                .OrderByDescending(x => x.CompletedAtUtc).FirstOrDefault();
            var leadingGap = weak.Length > 0 && names.TryGetValue(weak[0].CompetencyId, out var competency)
                ? competency.SkillNameSnapshot : null;
            var reason = prior?.OverallScore is < 70m
                ? $"Previous interview scored {prior.OverallScore:0.#}; reassess after targeted practice."
                : leadingGap is not null
                    ? $"{leadingGap} is a high-priority gap for this role."
                    : completedProjects > 0
                        ? "Completed project evidence supports a role-specific mock interview."
                        : "Validate current proficiency with a role-specific mock interview.";
            return new InterviewRecommendationResponse(version.Id, version.Definition.Name, version.Type,
                version.Difficulty, reason);
        }).ToArray();
    }

    public async Task<InterviewVersionResponse> VersionAsync(Guid actor, Guid versionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var version = await store.VersionAsync(versionId, ct) ?? throw new NotFoundException("Interview not found.");
        if (version.Status != AcceleratorInterviewCatalogStatus.Published || !version.Definition.IsActive)
            throw new NotFoundException("Interview not found.");
        return new InterviewVersionResponse(version.Id, version.Definition.Name, version.Definition.Description, version.Type,
            version.Difficulty, version.EstimatedMinutes, version.Sections.OrderBy(x => x.Sequence).Select(s =>
                new InterviewSectionResponse(s.Id, s.Sequence, s.Title, s.Type, s.IsRequired,
                    [], s.Questions.Any(q => q.IsActive && q.Kind == AcceleratorInterviewQuestionKind.AiGenerated),
                    s.AllowStaticFallback && s.Questions.Count(q => q.IsActive &&
                        q.Kind == AcceleratorInterviewQuestionKind.Static) >= s.QuestionCount)).ToArray());
    }

    public Task<InterviewSessionResponse> StartAsync(Guid actor, StartInterviewRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        if (request.RequestKey == Guid.Empty || request.VersionId == Guid.Empty) throw Invalid("Interview request key and version are required.");
        var goal = await Goal(actor, request.GoalId, ct);
        var hash = Hash(new { request.GoalId, request.VersionId, request.UseAiQuestions });
        var prior = await store.SessionByKeyAsync(actor, request.RequestKey, ct);
        if (prior is not null)
        {
            if (prior.RequestHash != hash) throw new ConflictException("Interview request key was reused with different input.");
            return Dto(prior, await store.AnswersAsync(actor, prior.Id, ct), await store.EvaluationsAsync(actor, prior.Id, ct));
        }
        var version = await store.VersionAsync(request.VersionId, ct) ?? throw new NotFoundException("Interview not found.");
        if (version.Status != AcceleratorInterviewCatalogStatus.Published || !version.Definition.IsActive ||
            version.Definition.TargetRoleId != goal.RoleId) throw new ConflictException("Interview is not available for this goal.");
        var session = new AcceleratorInterviewSession { UserId = actor, GoalId = goal.Id,
            InterviewVersionId = version.Id, RequestKey = request.RequestKey, RequestHash = hash,
            Status = AcceleratorInterviewSessionStatus.InProgress, StartedAtUtc = Now };
        foreach (var section in version.Sections.OrderBy(x => x.Sequence))
        {
            var aiAvailable = section.Questions.Any(q => q.IsActive && q.Kind == AcceleratorInterviewQuestionKind.AiGenerated);
            var selected = section.Questions.Where(q => q.IsActive && q.Kind == AcceleratorInterviewQuestionKind.Static &&
                    (!request.UseAiQuestions || !aiAvailable))
                .OrderBy(q => q.Sequence).ThenBy(q => q.Id).Take(section.QuestionCount).ToArray();
            if (selected.Length != section.QuestionCount && !aiAvailable)
                throw new ConflictException("Published interview question bank is incomplete.");
            foreach (var question in selected) session.Questions.Add(new AcceleratorSessionQuestion
            {
                SessionId = session.Id, SourceQuestionId = question.Id, SectionId = section.Id,
                CompetencyId = question.CompetencyId, Sequence = question.Sequence, Type = question.Type,
                PublicPrompt = question.PublicPrompt, AssignedAtUtc = Now
            });
        }
        store.Add(session);
        await Audit(actor, AuditAction.Create, "AcceleratorInterviewSession", session.Id, ct);
        return Dto(session, [], []);
    }, ct);

    public async Task<InterviewSessionResponse> SessionAsync(Guid actor, Guid sessionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var session = await RequiredSession(actor, sessionId, ct);
        return Dto(session, await store.AnswersAsync(actor, sessionId, ct), await store.EvaluationsAsync(actor, sessionId, ct));
    }

    public async Task<InterviewSessionResponse> AdminSessionAsync(Guid adminId, Guid candidateId, Guid sessionId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        var session = await RequiredSession(candidateId, sessionId, ct);
        return Dto(session, await store.AnswersAsync(candidateId, sessionId, ct),
            await store.EvaluationsAsync(candidateId, sessionId, ct));
    }

    public async Task<InterviewAdminAnswerResponse> AdminAnswerAsync(Guid adminId, Guid candidateId, Guid sessionId,
        Guid answerId, CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        _ = await RequiredSession(candidateId, sessionId, ct);
        var answer = (await store.AnswersAsync(candidateId, sessionId, ct)).SingleOrDefault(x => x.Id == answerId)
            ?? throw new NotFoundException("Interview answer not found.");
        var evaluation = await store.EvaluationAsync(candidateId, answerId, ct);
        return new InterviewAdminAnswerResponse(answer.Id, answer.SessionId, answer.SessionQuestionId,
            answer.Content, answer.SubmittedAtUtc, evaluation is null ? null : EvaluationDto(evaluation));
    }

    public async Task<InterviewQuestionResponse?> NextQuestionAsync(Guid actor, Guid sessionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var session = await RequiredSession(actor, sessionId, ct);
        if (session.Status != AcceleratorInterviewSessionStatus.InProgress) return null;
        var answers = await store.AnswersAsync(actor, sessionId, ct);
        var next = session.Questions.OrderBy(x => x.AssignedAtUtc).ThenBy(x => x.SectionId).ThenBy(x => x.Sequence)
            .FirstOrDefault(x => !answers.Any(a => a.SessionQuestionId == x.Id));
        return next is null ? null : new InterviewQuestionResponse(next.Id, next.SectionId, next.ParentQuestionId,
            next.Sequence, next.Type, next.PublicPrompt, false, false);
    }

    public Task<InterviewQuestionResponse> AssignStaticFallbackAsync(Guid actor, Guid sessionId, Guid sectionId,
        Guid requestKey, string requestHash, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        var session = await RequiredSession(actor, sessionId, ct);
        var prior = session.Questions.SingleOrDefault(x => x.GenerationRequestKey == requestKey);
        if (prior is not null)
        {
            if (prior.SectionId != sectionId || prior.GenerationRequestHash != requestHash)
                throw new ConflictException("Generation request key was reused with different input.");
            return QuestionDto(prior);
        }
        if (session.Status != AcceleratorInterviewSessionStatus.InProgress || session.Questions.Count >= 100)
            throw new ConflictException("Interview cannot accept another question.");
        var version = await store.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        var section = version.Sections.SingleOrDefault(x => x.Id == sectionId)
            ?? throw new NotFoundException("Interview section not found.");
        if (!section.AllowStaticFallback) throw new AcceleratorProviderUnavailableException();
        var assigned = session.Questions.Where(x => x.SectionId == sectionId && !x.ParentQuestionId.HasValue).ToArray();
        if (assigned.Length >= section.QuestionCount) throw new ConflictException("Interview section already has enough questions.");
        var source = section.Questions.Where(x => x.IsActive && x.Kind == AcceleratorInterviewQuestionKind.Static &&
                assigned.All(a => a.SourceQuestionId != x.Id))
            .OrderBy(x => x.Sequence).ThenBy(x => x.Id).FirstOrDefault()
            ?? throw new AcceleratorProviderUnavailableException();
        var snapshot = new AcceleratorSessionQuestion { SessionId = session.Id, SourceQuestionId = source.Id,
            SectionId = section.Id, CompetencyId = source.CompetencyId,
            Sequence = session.Questions.Where(x => x.SectionId == sectionId).Select(x => x.Sequence)
                .DefaultIfEmpty(0).Max() + 1,
            Type = source.Type, PublicPrompt = source.PublicPrompt, AssignedAtUtc = Now,
            GenerationRequestKey = requestKey, GenerationRequestHash = requestHash,
            GenerationProvider = "ApprovedStaticFallback", FollowUpEligible = true };
        session.Questions.Add(snapshot); session.Revision = Guid.NewGuid();
        await Audit(actor, AuditAction.Create, "AcceleratorSessionQuestion", snapshot.Id, ct);
        return QuestionDto(snapshot);
    }, ct);

    // Trusted provider/reconciliation boundary. The server supplies every ownership and provenance field.
    public Task<InterviewQuestionResponse> RecordGeneratedQuestionAsync(Guid actor, Guid sessionId, Guid sectionId,
        Guid sourceQuestionId, Guid dispatchId, Guid requestKey, string requestHash, string provider, string model,
        string structuredJson, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var session = await RequiredSession(actor, sessionId, ct);
        var prior = session.Questions.SingleOrDefault(x => x.GenerationRequestKey == requestKey ||
            x.GenerationDispatchId == dispatchId);
        if (prior is not null)
        {
            if (prior.SectionId != sectionId || prior.GenerationRequestHash != requestHash ||
                prior.GenerationDispatchId != dispatchId)
                throw new ConflictException("Generated question conflicts with persisted dispatch.");
            return QuestionDto(prior);
        }
        if (session.Status != AcceleratorInterviewSessionStatus.InProgress || session.Questions.Count >= 100)
            throw new ConflictException("Interview cannot accept another question.");
        var version = await store.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        var section = version.Sections.SingleOrDefault(x => x.Id == sectionId)
            ?? throw new ConflictException("Historical interview section is missing.");
        var source = section.Questions.SingleOrDefault(x => x.Id == sourceQuestionId && x.IsActive &&
            x.Kind == AcceleratorInterviewQuestionKind.AiGenerated)
            ?? throw new ConflictException("Published section does not permit AI question generation.");
        var assigned = session.Questions.Where(x => x.SectionId == sectionId && !x.ParentQuestionId.HasValue).ToArray();
        if (assigned.Length >= section.QuestionCount) throw new ConflictException("Interview section already has enough questions.");
        var payload = InterviewQuestionGenerationPolicy.Parse(structuredJson, version, section, source);
        var snapshot = new AcceleratorSessionQuestion { SessionId = session.Id, SourceQuestionId = source.Id,
            SectionId = section.Id, CompetencyId = payload.CompetencyIds[0],
            Sequence = session.Questions.Where(x => x.SectionId == sectionId).Select(x => x.Sequence)
                .DefaultIfEmpty(0).Max() + 1,
            Type = payload.QuestionType, PublicPrompt = payload.QuestionText.Trim(), AssignedAtUtc = Now,
            GenerationDispatchId = dispatchId, GenerationRequestKey = requestKey,
            GenerationRequestHash = requestHash, GenerationProvider = provider, GenerationModel = model,
            CompetencyIdsJson = JsonSerializer.Serialize(payload.CompetencyIds),
            GenerationMetadataJson = payload.GenerationMetadata, FollowUpEligible = payload.FollowUpEligible };
        session.Questions.Add(snapshot); session.Revision = Guid.NewGuid();
        await Audit(actor, AuditAction.Create, "AcceleratorSessionQuestion", snapshot.Id, ct);
        return QuestionDto(snapshot);
    }, ct);

    public async Task<IReadOnlyList<InterviewSessionResponse>> HistoryAsync(Guid actor, Guid goalId, int page, int pageSize, CancellationToken ct)
    {
        await access.Candidate(actor, ct); AcceleratorService.Page(page, pageSize);
        _ = await Goal(actor, goalId, ct);
        var rows = await store.SessionsAsync(actor, goalId, (page - 1) * pageSize, pageSize, ct);
        var output = new List<InterviewSessionResponse>(rows.Count);
        foreach (var row in rows)
            output.Add(Dto(row, await store.AnswersAsync(actor, row.Id, ct), await store.EvaluationsAsync(actor, row.Id, ct)));
        return output;
    }

    public Task<InterviewAnswerResponse> SubmitAnswerAsync(Guid actor, Guid sessionId,
        SubmitInterviewAnswerRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        if (request.RequestKey == Guid.Empty || request.QuestionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 16000)
            throw Invalid("Interview answer is invalid.");
        var session = await RequiredSession(actor, sessionId, ct);
        var hash = Hash(new { request.QuestionId, request.Content });
        var prior = await store.AnswerByKeyAsync(actor, sessionId, request.RequestKey, ct);
        if (prior is not null)
        {
            if (prior.RequestHash != hash) throw new ConflictException("Answer request key was reused with different input.");
            return AnswerDto(prior);
        }
        if (session.Status != AcceleratorInterviewSessionStatus.InProgress || session.Revision != request.ExpectedRevision)
            throw new ConflictException("Interview changed or is no longer accepting answers.");
        if (!session.Questions.Any(q => q.Id == request.QuestionId)) throw new NotFoundException("Session question not found.");
        if (await store.AnswerAsync(actor, sessionId, request.QuestionId, ct) is not null)
            throw new ConflictException("Question already answered.");
        var answer = new AcceleratorInterviewAnswer { SessionId = sessionId, SessionQuestionId = request.QuestionId,
            RequestKey = request.RequestKey, RequestHash = hash, Content = request.Content.Trim(), SubmittedAtUtc = Now };
        store.Add(answer);
        var assigned = session.Questions.Single(x => x.Id == request.QuestionId);
        if (!assigned.ParentQuestionId.HasValue && session.Questions.Count < 100)
        {
            var version = await store.VersionAsync(session.InterviewVersionId, ct)
                ?? throw new ConflictException("Historical interview version is missing.");
            var section = version.Sections.SingleOrDefault(x => x.Id == assigned.SectionId)
                ?? throw new ConflictException("Historical interview section is missing.");
            var source = section.Questions.SingleOrDefault(x => x.Id == assigned.SourceQuestionId)
                ?? throw new ConflictException("Historical interview question is missing.");
            var currentFollowUps = session.Questions.Count(x => x.ParentQuestionId == assigned.Id);
            if (assigned.FollowUpEligible && !string.IsNullOrWhiteSpace(source.FollowUpPrompt) &&
                currentFollowUps < section.MaxFollowUpsPerQuestion)
                session.Questions.Add(new AcceleratorSessionQuestion { SessionId = session.Id,
                    SourceQuestionId = source.Id, SectionId = section.Id, CompetencyId = source.CompetencyId,
                    ParentQuestionId = assigned.Id, Sequence = session.Questions.Where(x => x.SectionId == section.Id)
                        .Max(x => x.Sequence) + 1, Type = AcceleratorInterviewQuestionType.FollowUp,
                    PublicPrompt = source.FollowUpPrompt, AssignedAtUtc = Now });
        }
        session.Revision = Guid.NewGuid();
        await Audit(actor, AuditAction.Create, "AcceleratorInterviewAnswer", answer.Id, ct);
        return AnswerDto(answer);
    }, ct);

    public Task<InterviewEvaluationResponse> EvaluateDeterministicAsync(Guid actor, Guid sessionId, Guid answerId,
        CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        var session = await RequiredSession(actor, sessionId, ct);
        var answer = (await store.AnswersAsync(actor, sessionId, ct)).SingleOrDefault(x => x.Id == answerId)
            ?? throw new NotFoundException("Interview answer not found.");
        var existing = await store.EvaluationAsync(actor, answer.Id, ct);
        if (existing is not null)
        {
            if (existing.Evaluator != AcceleratorInterviewEvaluationMode.Deterministic)
                throw new ConflictException("Answer was evaluated with a different method.");
            return EvaluationDto(existing);
        }
        var question = session.Questions.SingleOrDefault(x => x.Id == answer.SessionQuestionId)
            ?? throw new ConflictException("Assigned interview question is missing.");
        var version = await store.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        var source = version.Sections.SelectMany(x => x.Questions).SingleOrDefault(x => x.Id == question.SourceQuestionId)
            ?? throw new ConflictException("Historical interview question is missing.");
        if (question.ParentQuestionId.HasValue || source.EvaluationMode != AcceleratorInterviewEvaluationMode.Deterministic)
            throw new ConflictException("Question requires authorized review.");
        var score = InterviewScoringPolicy.Score(source, version.Criteria, answer.Content);
        var evaluation = CreateEvaluation(answer.Id, AcceleratorInterviewEvaluationMode.Deterministic, score.Observations,
            version.Criteria, "Validated against published objective criteria.", 1m);
        store.Add(evaluation);
        await Audit(actor, AuditAction.Confirm, "AcceleratorInterviewEvaluation", evaluation.Id, ct);
        return EvaluationDto(evaluation);
    }, ct);

    public Task<InterviewEvaluationResponse> EvaluateManualAsync(Guid adminId, Guid sessionId, Guid answerId,
        ManualInterviewEvaluationRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(adminId, ct);
        var session = await RequiredSession(request.CandidateId, sessionId, ct);
        var answer = (await store.AnswersAsync(request.CandidateId, sessionId, ct)).SingleOrDefault(x => x.Id == answerId)
            ?? throw new NotFoundException("Interview answer not found.");
        if (request.Observations is null) throw Invalid("Manual rubric observations are required.");
        var existing = await store.EvaluationAsync(request.CandidateId, answerId, ct);
        if (existing is not null)
        {
            if (existing.Evaluator != AcceleratorInterviewEvaluationMode.Manual ||
                !SameScores(existing, request.Observations))
                throw new ConflictException("Answer already has a different evaluation.");
            return EvaluationDto(existing);
        }
        var version = await store.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        var evaluation = CreateEvaluation(answer.Id, AcceleratorInterviewEvaluationMode.Manual, request.Observations,
            version.Criteria, "Administrator evaluated the published rubric criteria.", 1m);
        store.Add(evaluation);
        await Audit(adminId, AuditAction.Confirm, "AcceleratorInterviewEvaluation", evaluation.Id, ct);
        return EvaluationDto(evaluation);
    }, ct);

    // Trusted provider/reconciliation boundary. Candidate input never supplies rubric scores.
    public Task<InterviewEvaluationResponse> RecordVerifiedAiAsync(Guid candidateId, Guid sessionId, Guid answerId,
        string structuredJson, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var session = await RequiredSession(candidateId, sessionId, ct);
        var answer = (await store.AnswersAsync(candidateId, sessionId, ct)).SingleOrDefault(x => x.Id == answerId)
            ?? throw new NotFoundException("Interview answer not found.");
        var existing = await store.EvaluationAsync(candidateId, answerId, ct);
        var assigned = session.Questions.SingleOrDefault(x => x.Id == answer.SessionQuestionId)
            ?? throw new ConflictException("Assigned interview question is missing.");
        var version = await store.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        var source = version.Sections.SelectMany(x => x.Questions).SingleOrDefault(x => x.Id == assigned.SourceQuestionId)
            ?? throw new ConflictException("Historical interview question is missing.");
        if (source.EvaluationMode != AcceleratorInterviewEvaluationMode.Ai)
            throw new ConflictException("Question does not permit AI evaluation.");
        var observations = InterviewAiResultParser.Parse(structuredJson, version.Criteria);
        if (existing is not null)
        {
            if (existing.Evaluator != AcceleratorInterviewEvaluationMode.Ai || !SameScores(existing, observations))
                throw new ConflictException("Answer already has a different verified evaluation.");
            return EvaluationDto(existing);
        }
        var evaluation = CreateEvaluation(answer.Id, AcceleratorInterviewEvaluationMode.Ai, observations,
            version.Criteria, "Provider evaluation against published rubric criteria.", .6m);
        store.Add(evaluation);
        await Audit(candidateId, AuditAction.Confirm, "AcceleratorInterviewEvaluation", evaluation.Id, ct);
        return EvaluationDto(evaluation);
    }, ct);

    public async Task<InterviewEvaluationResponse?> EvaluationAsync(Guid actor, Guid sessionId, Guid answerId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        _ = await RequiredSession(actor, sessionId, ct);
        if (!(await store.AnswersAsync(actor, sessionId, ct)).Any(x => x.Id == answerId)) throw new NotFoundException("Answer not found.");
        var evaluation = await store.EvaluationAsync(actor, answerId, ct);
        return evaluation is null ? null : EvaluationDto(evaluation);
    }

    public Task<InterviewReportResponse> CompleteAsync(Guid actor, Guid sessionId, CompleteInterviewRequest request,
        CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Candidate(actor, ct);
        var session = await RequiredSession(actor, sessionId, ct);
        if (session.Status == AcceleratorInterviewSessionStatus.Completed) return await ReportCore(actor, session, ct);
        if (session.Status != AcceleratorInterviewSessionStatus.InProgress || session.Revision != request.ExpectedRevision)
            throw new ConflictException("Interview changed or cannot be completed.");
        var answers = await store.AnswersAsync(actor, sessionId, ct);
        var evaluations = await store.EvaluationsAsync(actor, sessionId, ct);
        var version = await store.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        if (version.Sections.Where(x => x.IsRequired).Any(section =>
            session.Questions.Count(q => q.SectionId == section.Id && !q.ParentQuestionId.HasValue) < section.QuestionCount))
            throw new ConflictException("Required interview sections have unassigned questions.");
        var requiredQuestions = session.Questions.Where(q => version.Sections.Any(s => s.Id == q.SectionId && s.IsRequired)).ToArray();
        if (requiredQuestions.Any(q => !answers.Any(a => a.SessionQuestionId == q.Id && evaluations.Any(e => e.AnswerId == a.Id))))
            throw new ConflictException("Required interview questions must be answered and evaluated.");
        if (evaluations.Count == 0) throw new ConflictException("Interview has no evaluations.");
        session.OverallScore = decimal.Round(evaluations.Average(x => x.Score), 2);
        session.Status = AcceleratorInterviewSessionStatus.Completed;
        session.SubmittedAtUtc = Now; session.CompletedAtUtc = Now; session.Revision = Guid.NewGuid();
        var newEvidence = await RecordEvidence(actor, session, answers, evaluations, version, ct);
        var snapshot = await SnapshotAsync(actor, session.GoalId, session.Id, newEvidence, ct);
        session.ReadinessImpact = snapshot.InterviewPerformance;
        await Audit(actor, AuditAction.Confirm, "AcceleratorInterviewSession", session.Id, ct);
        return await ReportCore(actor, session, ct, snapshot);
    }, ct);

    public async Task<InterviewReportResponse> ReportAsync(Guid actor, Guid sessionId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var session = await RequiredSession(actor, sessionId, ct);
        if (session.Status != AcceleratorInterviewSessionStatus.Completed) throw new ConflictException("Interview is not complete.");
        return await ReportCore(actor, session, ct);
    }

    private async Task<InterviewReportResponse> ReportCore(Guid actor, AcceleratorInterviewSession session, CancellationToken ct,
        AcceleratorReadinessSnapshot? currentOverride = null)
    {
        var answers = await store.AnswersAsync(actor, session.Id, ct);
        var evaluations = await store.EvaluationsAsync(actor, session.Id, ct);
        var version = await store.VersionAsync(session.InterviewVersionId, ct)
            ?? throw new ConflictException("Historical interview version is missing.");
        var scored = from question in session.Questions
                     join answer in answers on question.Id equals answer.SessionQuestionId
                     join evaluation in evaluations on answer.Id equals evaluation.AnswerId
                     select new { question, evaluation };
        var sections = scored.GroupBy(x => x.question.SectionId)
            .Select(x => new InterviewSectionScoreResponse(x.Key, decimal.Round(x.Average(y => y.evaluation.Score), 2))).ToArray();
        var competencies = scored.Where(x => x.question.CompetencyId.HasValue)
            .GroupBy(x => x.question.CompetencyId!.Value)
            .Select(x => new InterviewCompetencyScoreResponse(x.Key, decimal.Round(x.Average(y => y.evaluation.Score), 2))).ToArray();
        var names = (await store.CompetenciesAsync(competencies.Select(x => x.CompetencyId).ToArray(), ct))
            .ToDictionary(x => x.Id, x => x.SkillNameSnapshot);
        string Name(Guid id) => names.GetValueOrDefault(id, "Unmapped competency");
        var weak = competencies.Where(x => x.Score < 70m).OrderBy(x => x.Score)
            .Select(x => Name(x.CompetencyId)).ToArray();
        var strong = competencies.Where(x => x.Score >= 70m).OrderByDescending(x => x.Score)
            .Select(x => Name(x.CompetencyId)).ToArray();
        var previous = (await store.ReadinessHistoryAsync(actor, session.GoalId, 0, 20, ct))
            .FirstOrDefault(x => x.TriggerInterviewSessionId != session.Id);
        var current = currentOverride ?? await store.ReadinessForSessionAsync(session.Id, ct);
        return new InterviewReportResponse(session.Id, session.OverallScore ?? 0m, sections, competencies,
            strong, weak, weak.Length > 0 ? [$"Practise {weak[0]} and revisit its roadmap activity."] :
                ["Continue role-specific practice and verify skills with projects."],
            (current?.Total ?? 0m) - (previous?.Total ?? 0m));
    }

    private async Task<IReadOnlyList<AcceleratorSkillEvidence>> RecordEvidence(Guid actor, AcceleratorInterviewSession session,
        IReadOnlyList<AcceleratorInterviewAnswer> answers, IReadOnlyList<AcceleratorInterviewEvaluation> evaluations,
        AcceleratorInterviewVersion version, CancellationToken ct)
    {
        var existing = await phase2.EvidenceAsync(actor, session.GoalId, ct);
        var roadmap = await phase2.ActiveRoadmapAsync(actor, session.GoalId, ct);
        var activities = roadmap is null ? [] : await phase2.ActivitiesAsync(actor, roadmap.Id, ct);
        var newEvidence = new List<AcceleratorSkillEvidence>();
        foreach (var evaluation in evaluations)
        {
            var answer = answers.Single(x => x.Id == evaluation.AnswerId);
            var question = session.Questions.Single(x => x.Id == answer.SessionQuestionId);
            if (!question.CompetencyId.HasValue) continue;
            var proficiency = evaluation.Score >= 90m ? AcceleratorProficiency.Intermediate :
                evaluation.Score >= 70m ? AcceleratorProficiency.Developing : AcceleratorProficiency.Beginner;
            var item = new AcceleratorSkillEvidence { UserId = actor, GoalId = session.GoalId,
                CompetencyId = question.CompetencyId.Value, Source = AcceleratorEvidenceSource.MockInterview,
                Proficiency = proficiency, Confidence = evaluation.Evaluator == AcceleratorInterviewEvaluationMode.Deterministic ? .55m : .4m,
                ScoringVersion = "interview-rubric-v1", Score = evaluation.Score, EvaluatedAtUtc = evaluation.EvaluatedAtUtc,
                InterviewSessionId = session.Id, SessionQuestionId = question.Id, InterviewEvaluationId = evaluation.Id };
            ProjectEvidencePolicy.PreserveStrongerVerifiedProficiency([item], existing);
            phase2.Add(item);
            newEvidence.Add(item);
            var node = roadmap?.Nodes.SingleOrDefault(x => x.CompetencyId == item.CompetencyId);
            var activity = activities.FirstOrDefault(x => x.RoadmapNodeId == node?.Id);
            if (activity is null || node is null) continue;
            var key = DeterministicKey(evaluation.Id, activity.Id);
            if (await phase2.ProgressEventByKeyAsync(actor, key, ct) is not null) continue;
            phase2.Add(new AcceleratorProgressEvent { UserId = actor, ActivityId = activity.Id, RequestKey = key,
                Kind = AcceleratorProgressKind.EvidenceSubmitted, OccurredAtUtc = Now,
                EvidenceJson = JsonSerializer.Serialize(new { source = "MockInterview", sessionId = session.Id,
                    evaluationId = evaluation.Id, competencyId = item.CompetencyId, score = item.Score }) });
            if (node.Status == AcceleratorNodeStatus.Completed) continue;
            node.ProgressPercent = Math.Max(node.ProgressPercent, Math.Min(75, (int)(item.Confidence * 100)));
            if (node.Status is AcceleratorNodeStatus.Available or AcceleratorNodeStatus.NeedsPractice)
                node.Status = AcceleratorNodeStatus.InProgress;
            if (activity.Status == AcceleratorNodeStatus.Available)
            {
                activity.Status = AcceleratorNodeStatus.InProgress; activity.Revision = Guid.NewGuid();
            }
        }
        return newEvidence;
    }

    private async Task<AcceleratorReadinessSnapshot> SnapshotAsync(Guid actor, Guid goalId, Guid sessionId,
        IReadOnlyList<AcceleratorSkillEvidence> newEvidence, CancellationToken ct)
    {
        var prior = await store.ReadinessForSessionAsync(sessionId, ct);
        if (prior is not null) return prior;
        var goal = await Goal(actor, goalId, ct);
        var requirements = await phase2.RequirementsAsync(goal.RoleId, ct);
        var evidence = (await phase2.EvidenceAsync(actor, goalId, ct)).Concat(newEvidence).ToArray();
        var covered = requirements.Count(x => evidence.Any(e => e.CompetencyId == x.CompetencyId &&
            e.Proficiency >= (AcceleratorProficiency)x.ExpectedProficiency && e.Confidence >= .6m));
        var skill = requirements.Count == 0 ? 0m : decimal.Round(100m * covered / requirements.Count, 2);
        var interview = (await store.SessionsAsync(actor, goalId, 0, 100, ct))
            .Where(x => x.Status == AcceleratorInterviewSessionStatus.Completed && x.OverallScore.HasValue)
            .Select(x => x.OverallScore!.Value).DefaultIfEmpty(0m).Average();
        var practiceSessions = (await practice.PracticeSessionsAsync(actor, 0, 100, ct)).Where(x => x.GoalId == goalId).ToArray();
        var practiceScores = new List<decimal>();
        foreach (var row in practiceSessions)
        {
            var evaluations = await practice.PracticeEvaluationsAsync(actor, row.Id, ct);
            practiceScores.AddRange(evaluations.Where(x => x.MaximumScore > 0).Select(x => 100m * x.Score / x.MaximumScore));
        }
        var practicePerformance = practiceScores.Count == 0 ? 0m : decimal.Round(practiceScores.Average(), 2);
        var projectScores = (await projects.CandidateProjectsAsync(actor, 0, 100, ct))
            .Where(x => x.GoalId == goalId && x.FinalScore.HasValue).Select(x => x.FinalScore!.Value).ToArray();
        var projectPerformance = projectScores.Length == 0 ? 0m : decimal.Round(projectScores.Average(), 2);
        var snapshot = new AcceleratorReadinessSnapshot { UserId = actor, GoalId = goalId,
            TriggerInterviewSessionId = sessionId, SkillCoverage = skill, PracticePerformance = practicePerformance,
            ProjectPerformance = projectPerformance, InterviewPerformance = decimal.Round(interview, 2),
            CalculationVersion = CareerReadinessPolicy.Version,
            Total = CareerReadinessPolicy.Calculate(skill, practicePerformance, projectPerformance, interview),
            CalculatedAtUtc = Now,
            Explanation = "Readiness v1 weights verified skill coverage 40%, practice 10%, completed projects 20%, and completed interviews 30%. Missing components score zero." };
        store.Add(snapshot);
        return snapshot;
    }

    private static AcceleratorInterviewEvaluation CreateEvaluation(Guid answerId, AcceleratorInterviewEvaluationMode mode,
        IReadOnlyList<InterviewCriterionObservation> observations, ICollection<AcceleratorInterviewRubricCriterion> criteria,
        string feedback, decimal confidence)
    {
        if (observations.Count != criteria.Count || observations.Select(x => x.CriterionId).Distinct().Count() != observations.Count ||
            criteria.Sum(x => x.Weight) != 100m) throw Invalid("Evaluation must score every published criterion exactly once.");
        var row = new AcceleratorInterviewEvaluation { AnswerId = answerId, Evaluator = mode,
            Confidence = confidence, Feedback = feedback, EvaluatedAtUtc = DateTime.UtcNow };
        foreach (var criterion in criteria)
        {
            var score = observations.SingleOrDefault(x => x.CriterionId == criterion.Id)
                ?? throw Invalid("Evaluation omitted a rubric criterion.");
            if (score.Score < 0 || score.Score > criterion.MaximumScore) throw Invalid("Criterion score is outside the rubric range.");
            row.CriterionScores.Add(new AcceleratorInterviewCriterionScore { EvaluationId = row.Id,
                CriterionId = criterion.Id, Score = score.Score });
            row.Score += criterion.Weight * score.Score / criterion.MaximumScore;
        }
        row.Score = decimal.Round(row.Score, 2);
        return row;
    }

    private static InterviewSessionResponse Dto(AcceleratorInterviewSession session,
        IReadOnlyList<AcceleratorInterviewAnswer> answers, IReadOnlyList<AcceleratorInterviewEvaluation> evaluations) =>
        new(session.Id, session.InterviewVersionId, session.Status, session.Revision, session.OverallScore,
            session.StartedAtUtc, session.CompletedAtUtc,
            session.Questions.OrderBy(x => x.SectionId).ThenBy(x => x.Sequence).Select(q =>
                new InterviewQuestionResponse(q.Id, q.SectionId, q.ParentQuestionId, q.Sequence, q.Type,
                    q.PublicPrompt, answers.Any(a => a.SessionQuestionId == q.Id),
                    answers.Any(a => a.SessionQuestionId == q.Id && evaluations.Any(e => e.AnswerId == a.Id)))).ToArray());
    private static InterviewAnswerResponse AnswerDto(AcceleratorInterviewAnswer row) =>
        new(row.Id, row.SessionQuestionId, row.SubmittedAtUtc);
    private static InterviewQuestionResponse QuestionDto(AcceleratorSessionQuestion row) =>
        new(row.Id, row.SectionId, row.ParentQuestionId, row.Sequence, row.Type, row.PublicPrompt, false, false);
    private static InterviewEvaluationResponse EvaluationDto(AcceleratorInterviewEvaluation row) =>
        new(row.Id, row.AnswerId, row.Evaluator, row.Score, row.Feedback, row.EvaluatedAtUtc);
    private static bool SameScores(AcceleratorInterviewEvaluation existing,
        IReadOnlyList<InterviewCriterionObservation> observations) =>
        existing.CriterionScores.Count == observations.Count &&
        existing.CriterionScores.All(x => observations.Count(y => y.CriterionId == x.CriterionId && y.Score == x.Score) == 1);
    private async Task<AcceleratorCareerGoal> Goal(Guid actor, Guid id, CancellationToken ct) =>
        await writes.GoalAsync(actor, id, ct) ?? throw new NotFoundException("Career goal not found.");
    private async Task<AcceleratorInterviewSession> RequiredSession(Guid actor, Guid id, CancellationToken ct) =>
        await store.SessionAsync(actor, id, ct) ?? throw new NotFoundException("Interview session not found.");
    private Task Audit(Guid actor, AuditAction action, string type, Guid id, CancellationToken ct) =>
        audit.AppendAsync(new AuditEvent(action, type, id.ToString(), Actor: new AuditActor(actor, "Candidate")), ct);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static Guid DeterministicKey(Guid a, Guid b) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{a:N}:{b:N}"))[..16]);
    private static BadRequestException Invalid(string message) => new(message, "accelerator_interview_invalid");
}
