using System.Globalization;
using System.Text.Json;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobPortal.Application.Features.AIResume;

public sealed class AIResumeService(IAIResumeRepository repository, IAIResumeProvider provider,
    IAIResumeSourceParser sourceParser, IAIResumeDocumentRenderer renderer, IPhonePeGateway phonePe,
    IOptions<AIResumeOptions> options, TimeProvider clock, ILogger<AIResumeService>? logger = null,
    IHostEnvironment? environment = null, IAIResumeMasterDocuments? masterDocuments = null) : IAIResumeService
{
    private const string ReturnPath = "/dashboard/resume-maker";
    private const int MaximumJobDescriptionLength = 20_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private AIResumeOptions Settings => options.Value;
    private static readonly Action<ILogger, string, Exception?> LogSourceSnapshot =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(7405, "AIResumeSourceSnapshot"),
            "AIResumeSourceSnapshot {Metadata}");
    private static readonly Action<ILogger, string, Exception?> LogAnalysisRejected =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(7406, "AIResumeAnalysisRejected"),
            "AIResumeAnalysisRejected {Diagnostic}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogSourceMissing =
        LoggerMessage.Define<Guid, Guid>(LogLevel.Warning, new EventId(7409, "AIResumeSourceMissing"),
            "AIResumeSourceMissing OwnerUserId={OwnerUserId} ResumeId={ResumeId}");
    private static readonly Action<ILogger, string, Exception?> LogPatchRejected =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(7404, "AIResumeGroundingRejected"), "AIResumeGroundingRejected {Diagnostic}");
    private static readonly Action<ILogger, int, int, int, string, Exception?> LogPatchCounts =
        LoggerMessage.Define<int, int, int, string>(LogLevel.Information, new EventId(7408, "AIResumeTailoringPatch"),
            "AIResumeTailoringPatch Proposed={Proposed} Accepted={Accepted} Rejected={Rejected} PreservationMode={PreservationMode}");

    public IReadOnlyList<AIResumePackage> Packages() => Settings.Packages
        .Select(x => new AIResumePackage(x.Code, x.Name, x.Price, x.Credits, x.IsPopular)).ToArray();

    public async Task<AIResumeCreditResponse> CreditsAsync(Guid userId, CancellationToken ct)
    {
        var wallet = await repository.WalletAsync(userId, ct);
        return wallet is null ? new(0, 0, 0, 0) : Wallet(wallet);
    }

    public async Task<AIResumeSessionResponse> CreateSessionAsync(Guid userId, CreateAIResumeSessionRequest request, CancellationToken ct)
    {
        if (request.SourceResumeId == Guid.Empty || (request.JobId.HasValue == !string.IsNullOrWhiteSpace(request.ExternalJobDescription)))
            throw Invalid("Provide one resume source and either a CareerHarbor job or an external job description.", "invalid_session");
        var candidate = await repository.CandidateAsync(userId, ct) ?? throw new NotFoundException("Candidate was not found.");
        if (candidate.ResumeProfile?.Id != request.SourceResumeId)
            throw new NotFoundException("Resume source was not found.");
        if (candidate.ResumeStorageKey is null) throw ReuploadRequired();
        ResumeMasterDocument? master = null;
        AIResumeSession? preparedSession = null;
        try
        {
            TailoredResumeContent source;
            try
            {
                if (masterDocuments is not null) master = await masterDocuments.CaptureAsync(candidate, ct);
                var parserCandidate = master is null ? candidate : new User { Id = candidate.Id, ResumeProfile = candidate.ResumeProfile,
                    ResumeStorageKey = master.StorageKey, ResumeFileName = "master" + master.Extension };
                source = await sourceParser.ParseAsync(parserCandidate, ct);
                if (master is not null) master = await masterDocuments!.BindAsync(userId, master, source, ct);
            }
            catch (ResumeStorageObjectNotFoundException)
            {
                if (logger is not null) LogSourceMissing(logger, userId, request.SourceResumeId, null);
                throw ReuploadRequired();
            }
            catch (UnsupportedResumeFormatException)
            {
                throw Invalid("This resume file format is not supported for AI Resume.", "unsupported_resume_format");
            }
            catch (Exception ex)
            {
                if (ex is InvalidDataException) throw Invalid("The uploaded resume could not be read or parsed. Please upload a valid PDF, DOC, or DOCX file.", "invalid_resume_source");
                throw;
            }

            string title, company, jd, sourceType; Guid? jobId = null;
            if (request.JobId is { } id)
            {
                var job = await repository.JobAsync(id, Now, ct) ?? throw new NotFoundException("Job was not found.");
                title = job.Title; company = job.Company.Name;
                jd = string.Join("\n", new[] { job.Title, job.Description, job.Requirements, job.Responsibilities }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                jobId = job.Id; sourceType = "CAREERHARBOR_JOB";
            }
            else
            {
                jd = request.ExternalJobDescription!.Trim();
                if (jd.Length is < 50 or > MaximumJobDescriptionLength) throw Invalid("Job description must be between 50 and 20,000 characters.", "invalid_job_description");
                title = SafeSnapshot(request.JobTitle, 200); company = SafeSnapshot(request.CompanyName, 200); sourceType = "EXTERNAL_JD";
            }
            if (jd.Length is < 50 or > MaximumJobDescriptionLength) throw Invalid("Job description must be between 50 and 20,000 characters.", "invalid_job_description");
            var evidence = ResumeEvidenceCatalog.Create(source);
            var session = new AIResumeSession
            {
                UserId = userId, SourceResumeId = request.SourceResumeId, JobId = jobId, SourceType = sourceType,
                JobTitle = title, CompanyName = company, JobDescription = jd,
                SourceJson = JsonSerializer.Serialize(source, Json), EvidenceJson = master is null ? JsonSerializer.Serialize(evidence, Json) :
                    JsonSerializer.Serialize(new ResumeSessionEvidence(2, evidence, master), Json),
                Status = AIResumeSessionStatus.Created
            };
            preparedSession = session;
            if (environment?.IsDevelopment() == true && logger is not null)
                LogSourceSnapshot(logger, JsonSerializer.Serialize(new
                {
                    SessionId = session.Id, SkillsCount = source.Skills.Length, ExperienceCount = source.Experience.Length,
                    ProjectsCount = source.Projects.Length, EducationCount = source.Education.Length,
                    CertificationsCount = source.Certifications.Length, AdditionalInfoCount = source.AdditionalInfo.Length,
                    EvidenceCount = evidence.Length, HasContact = !string.IsNullOrWhiteSpace(source.Contact.Name),
                    HasSummary = !string.IsNullOrWhiteSpace(source.ProfessionalSummary), SourceResumeId = session.SourceResumeId
                }), null);
            await repository.WriteAsync(userId, () => { repository.Add(session); return Task.FromResult(true); }, ct);
            return ToSession(session, null);
        }
        catch
        {
            if (master is not null)
            {
                if (preparedSession is null) await DeleteDocument(userId, master.StorageKey);
                else
                {
                    try
                    {
                        var persisted = await repository.SessionAsync(userId, preparedSession.Id, CancellationToken.None);
                        if (persisted is null || SessionMaster(persisted)?.StorageKey != master.StorageKey) await DeleteDocument(userId, master.StorageKey);
                    }
                    catch { /* An ambiguous commit must not delete a persisted session's master document. */ }
                }
            }
            throw;
        }
    }

    public async Task<AIResumeSessionResponse> GetSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, sessionId, ct);
        var recovered = false;
        if (session.Status == AIResumeSessionStatus.Analyzing && session.AnalysisLeaseUntilUtc <= Now && session.AnalysisOwner is { } analysisOwner)
        { await ReleaseAnalysisLease(userId, sessionId, analysisOwner, ct); recovered = true; }
        if (session.Status == AIResumeSessionStatus.Generating)
        {
            var active = await repository.ActiveGenerationAsync(userId, sessionId, ct);
            if (active is not null && active.LeaseUntilUtc <= Now)
            { await ReleaseGeneration(userId, active.RequestKey, active.Owner, ct); recovered = true; }
        }
        if (recovered) session = await RequiredSession(userId, sessionId, ct);
        return ToSession(session, await repository.LatestAsync(userId, sessionId, ct));
    }

    public async Task<AIResumeSessionResponse> AnalyzeAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, sessionId, ct);
        if (session.AnalysisJson is not null && session.Status is AIResumeSessionStatus.Analyzed or AIResumeSessionStatus.Generated)
            return ToSession(session, await repository.LatestAsync(userId, sessionId, ct));
        var owner = Guid.NewGuid();
        var acquired = await repository.WriteAsync(userId, async () =>
        {
            var current = await RequiredSession(userId, sessionId, ct);
            if (current.AnalysisJson is not null && current.Status is AIResumeSessionStatus.Analyzed or AIResumeSessionStatus.Generated) return false;
            if (current.Status == AIResumeSessionStatus.Analyzing && current.AnalysisLeaseUntilUtc > Now)
                throw new ConflictException("Resume analysis is already in progress.", "analysis_in_progress");
            current.Status = AIResumeSessionStatus.Analyzing; current.AnalysisOwner = owner;
            current.AnalysisLeaseUntilUtc = Now.AddSeconds(Settings.TimeoutSeconds + 30);
            return true;
        }, ct);
        if (!acquired) return await GetSessionAsync(userId, sessionId, ct);
        try
        {
            var source = Deserialize<TailoredResumeContent>(session.SourceJson);
            var result = await provider.AnalyzeAsync(new(AnalysisSource(source), session.JobDescription), ct);
            var failures = AIResumeAnalysisValidator.Rejections(result.Content, source.Skills);
            if (failures.Length > 0)
            {
                if (environment?.IsDevelopment() == true && logger is not null)
                    foreach (var failure in failures)
                        LogAnalysisRejected(logger, JsonSerializer.Serialize(new
                        {
                            failure.Category, failure.Reason, failure.Path, failure.Count, failure.HasValue,
                            Model = Settings.Model, result.InputTokens, result.OutputTokens
                        }), null);
                throw new AIResumeProviderException("invalid_analysis");
            }
            await repository.WriteAsync(userId, async () =>
            {
                var current = await RequiredSession(userId, sessionId, ct);
                if (current.Status != AIResumeSessionStatus.Analyzing || current.AnalysisOwner != owner)
                    throw new ConflictException("Analysis lease is no longer valid.", "analysis_lease_lost");
                current.AnalysisJson = JsonSerializer.Serialize(result.Content, Json); current.AnalysisModel = result.Model;
                current.AnalysisInputTokens = result.InputTokens; current.AnalysisOutputTokens = result.OutputTokens;
                current.AnalysisOwner = null; current.AnalysisLeaseUntilUtc = null; current.Status = AIResumeSessionStatus.Analyzed;
                return true;
            }, ct);
        }
        catch (OperationCanceledException) { await ReleaseAnalysisLease(userId, sessionId, owner, CancellationToken.None); throw; }
        catch (AIResumeProviderException ex)
        {
            await ReleaseAnalysisLease(userId, sessionId, owner, CancellationToken.None);
            throw new AppException("Resume analysis is temporarily unavailable.", 503, ex.Code);
        }
        catch
        {
            await ReleaseAnalysisLease(userId, sessionId, owner, CancellationToken.None);
            throw;
        }
        return await GetSessionAsync(userId, sessionId, ct);
    }

    public async Task<AIResumeCheckout> CheckoutAsync(Guid userId, AIResumeCheckoutRequest request, CancellationToken ct)
    {
        if (request.IdempotencyKey == Guid.Empty) throw Invalid("An idempotency key is required.", "invalid_idempotency_key");
        var package = Settings.Packages.SingleOrDefault(x => string.Equals(x.Code, request.PackageCode, StringComparison.Ordinal))
            ?? throw Invalid("Resume credit package was not found.", "invalid_package");
        if (request.SessionId.HasValue) _ = await RequiredSession(userId, request.SessionId.Value, ct);
        var now = Now; var owner = Guid.NewGuid();
        var purchaseId = await repository.WriteAsync(userId, async () =>
        {
            var current = await repository.PurchaseByKeyAsync(userId, request.IdempotencyKey, ct);
            if (current is not null)
            {
                if (current.PackageCode != package.Code || current.SessionId != request.SessionId)
                    throw new ConflictException("Idempotency key was already used for another purchase.", "idempotency_conflict");
                if (current.RedirectUrl is not null || current.Status == PaymentStatus.Paid) return current.Id;
                if (current.CheckoutLeaseUntilUtc > now) throw new ConflictException("Checkout is being prepared.", "checkout_in_progress");
                current.CheckoutOwner = owner; current.CheckoutLeaseUntilUtc = now.AddMinutes(2); return current.Id;
            }
            var purchase = new AIResumePurchase
            {
                UserId = userId, RequestKey = request.IdempotencyKey, SessionId = request.SessionId,
                PackageCode = package.Code, Credits = package.Credits, Amount = package.Price,
                CurrencyCode = "INR", MerchantOrderId = "air_" + Guid.NewGuid().ToString("N"),
                CheckoutOwner = owner, CheckoutLeaseUntilUtc = now.AddMinutes(2), Status = PaymentStatus.Created
            };
            repository.Add(purchase); return purchase.Id;
        }, ct);
        var row = await repository.PurchaseByKeyAsync(userId, request.IdempotencyKey, ct)
            ?? throw new InvalidOperationException("Resume purchase intent was not persisted.");
        if (row.RedirectUrl is not null || row.Status == PaymentStatus.Paid) return ToCheckout(row);
        if (row.CheckoutOwner != owner) throw new ConflictException("Checkout is being prepared.", "checkout_in_progress");
        try
        {
            var providerCheckout = await phonePe.CreateCheckoutAsync(row.MerchantOrderId, MinorUnits(row.Amount), ReturnPath, ct);
            await repository.WriteAsync(userId, async () =>
            {
                var current = await repository.PurchaseByKeyAsync(userId, request.IdempotencyKey, ct)
                    ?? throw new NotFoundException("Resume purchase was not found.");
                if (current.CheckoutOwner != owner || current.Status == PaymentStatus.Paid)
                    throw new ConflictException("Checkout lease is no longer valid.", "checkout_lease_lost");
                current.RedirectUrl = providerCheckout.RedirectUrl; current.Status = PaymentStatus.Pending;
                current.CheckoutOwner = null; current.CheckoutLeaseUntilUtc = null; return true;
            }, ct);
        }
        catch
        {
            await repository.WriteAsync(userId, async () =>
            {
                var current = await repository.PurchaseByKeyAsync(userId, request.IdempotencyKey, ct);
                if (current?.CheckoutOwner == owner) { current.CheckoutOwner = null; current.CheckoutLeaseUntilUtc = null; }
                return true;
            }, CancellationToken.None);
            throw;
        }
        return ToCheckout(await repository.PurchaseByKeyAsync(userId, request.IdempotencyKey, ct)
            ?? throw new NotFoundException("Resume purchase was not found."));
    }

    public async Task<AIResumeCheckout?> PendingCheckoutAsync(Guid userId, string merchantOrderId, CancellationToken ct)
    {
        var purchase = await repository.PurchaseForOwnerAsync(userId, merchantOrderId, ct);
        return purchase is null ? null : ToCheckout(purchase);
    }

    public async Task<AIResumeCheckout> PhonePeReturnAsync(Guid userId, string merchantOrderId, CancellationToken ct)
    {
        var purchase = await repository.PurchaseForOwnerAsync(userId, merchantOrderId, ct)
            ?? throw new NotFoundException("Resume purchase was not found.");
        if (purchase.Status != PaymentStatus.Paid) await VerifyAndApplyPurchase(purchase, ct);
        purchase = await repository.PurchaseForOwnerAsync(userId, merchantOrderId, ct)
            ?? throw new NotFoundException("Resume purchase was not found.");
        return ToCheckout(purchase);
    }

    public async Task<bool> ProcessPhonePeWebhookAsync(JobPortal.Application.Features.Payments.PhonePeWebhookRequest request, CancellationToken ct)
    {
        if (!phonePe.VerifyWebhookAuthorization(request.Authorization)) throw new UnauthorizedException("PhonePe webhook authentication failed.");
        var callback = phonePe.ParseCallback(request.RawBody);
        if (!callback.MerchantOrderId.StartsWith("air_", StringComparison.Ordinal)) return false;
        var purchase = await repository.PurchaseAsync(callback.MerchantOrderId, ct)
            ?? throw new NotFoundException("Resume purchase was not found.");
        if (purchase.Status == PaymentStatus.Paid) return true;
        await VerifyAndApplyPurchase(purchase, ct);
        return true;
    }

    public async Task<AIResumeResponse> GenerateAsync(Guid userId, Guid sessionId, AIResumeGenerationRequest request, CancellationToken ct)
    {
        if (request.IdempotencyKey == Guid.Empty) throw Invalid("An idempotency key is required.", "invalid_idempotency_key");
        var now = Now; var owner = Guid.NewGuid();
        var reservation = await repository.WriteAsync(userId, async () =>
        {
            var currentSession = await RequiredSession(userId, sessionId, ct);
            if (currentSession.AnalysisJson is null || currentSession.Status is AIResumeSessionStatus.Created or AIResumeSessionStatus.Analyzing)
                throw new ConflictException("Complete resume analysis before generating.", "analysis_required");
            var prior = await repository.GenerationByKeyAsync(userId, request.IdempotencyKey, ct);
            if (prior is not null)
            {
                if (prior.SessionId != sessionId) throw new ConflictException("Idempotency key was already used for another generation.", "idempotency_conflict");
                if (prior.Status == AIResumeGenerationStatus.Consumed)
                    return (Generation: prior, Source: Deserialize<TailoredResumeContent>(currentSession.SourceJson), Analysis: Deserialize<ResumeAnalysis>(currentSession.AnalysisJson), ExistingResume: await repository.ResumeForGenerationAsync(userId, prior.Id, ct));
            }
            var wallet = await repository.WalletAsync(userId, ct);
            var active = await repository.ActiveGenerationAsync(userId, sessionId, ct);
            if (active is not null)
            {
                if (active.LeaseUntilUtc > now) throw new ConflictException("Resume generation is already in progress.", "generation_in_progress");
                if (wallet is null || wallet.Reserved < 1) throw new InvalidOperationException("Resume credit reservation is inconsistent.");
                wallet.Reserved--; wallet.Balance++; active.Status = AIResumeGenerationStatus.Released;
                active.FailureCode = "reservation_expired";
                AddLedger(wallet, userId, null, active.Id, AIResumeCreditKind.Release, 1, -1, $"generation:{active.Id}:release:{active.Attempt}");
                currentSession.Status = await repository.LatestAsync(userId, sessionId, ct) is null
                    ? AIResumeSessionStatus.Analyzed : AIResumeSessionStatus.Generated;
            }
            if (wallet is null || wallet.Balance < 1) throw new ConflictException("Purchase resume credits before generating.", "insufficient_resume_credits");
            if (prior is null)
            {
                prior = new AIResumeGeneration { UserId = userId, SessionId = sessionId, RequestKey = request.IdempotencyKey };
                repository.Add(prior);
            }
            prior.Attempt++; prior.Owner = owner; prior.LeaseUntilUtc = now.AddSeconds(Settings.TimeoutSeconds + 120);
            prior.Status = AIResumeGenerationStatus.Reserved; prior.FailureCode = null;
            wallet.Balance--; wallet.Reserved++; currentSession.Status = AIResumeSessionStatus.Generating;
            AddLedger(wallet, userId, null, prior.Id, AIResumeCreditKind.Reservation, -1, 1,
                $"generation:{prior.Id}:reserve:{prior.Attempt}");
            return (Generation: prior, Source: Deserialize<TailoredResumeContent>(currentSession.SourceJson),
                Analysis: Deserialize<ResumeAnalysis>(currentSession.AnalysisJson), ExistingResume: (TailoredResume?)null);
        }, ct);
        if (reservation.ExistingResume is not null) return ToResume(reservation.ExistingResume);
        ResumeArtifact? stagedArtifact = null;
        try
        {
            var storedSession = await RequiredSession(userId, sessionId, ct);
            AIResumeProviderResult<TailoredResumeContent> generated;
            StoredTailoring? tailoring = null;
            if (masterDocuments is not null)
            {
                var master = SessionMaster(storedSession) ?? throw new AIResumeProviderException("master_snapshot_required");
                var editable = ResumePatchGuard.Targets(reservation.Source).Where(x => master.Extension != ".docx" || master.Bindings.Any(b => b.TargetId == x.Id)).ToArray();
                var raw = await provider.GenerateTailoringPatchAsync(new(reservation.Source, storedSession.JobDescription, reservation.Analysis,
                    ResumeEvidenceCatalog.Create(reservation.Source), editable), ct);
                var applied = ResumePatchGuard.Apply(reservation.Source, raw.Content, storedSession.JobDescription,
                    editable.Select(x => x.Id).ToHashSet(StringComparer.Ordinal), LogRejectedPatch);
                if (applied.AcceptedCount == 0) throw new AIResumeProviderException("no_safe_tailoring_changes");
                stagedArtifact = await masterDocuments.CreateArtifactAsync(userId, master, reservation.Source, applied, ct);
                tailoring = new(2, applied.Content, applied.Decisions, applied.EmphasizedSkillEvidenceIds, master, stagedArtifact);
                generated = new(applied.Content, raw.Model, raw.InputTokens, raw.OutputTokens);
                if (environment?.IsDevelopment() == true && logger is not null)
                    LogPatchCounts(logger, applied.Decisions.Length, applied.AcceptedCount, applied.Decisions.Count(x => x.Status == "rejected"),
                        masterDocuments.Capabilities(master).PreservationMode, null);
            }
            else generated = await provider.GenerateTailoredResumeAsync(new(reservation.Source, storedSession.JobDescription, reservation.Analysis), ct);
            var validation = await provider.ValidateTailoredResumeAsync(new(reservation.Source, generated.Content), ct);
            if (!validation.IsValid) throw new AIResumeProviderException("unsupported_claims");
            var result = await repository.WriteAsync(userId, async () =>
            {
                var current = await RequiredSession(userId, sessionId, ct);
                var generation = await repository.GenerationByKeyAsync(userId, request.IdempotencyKey, ct)
                    ?? throw new NotFoundException("Resume generation was not found.");
                if (generation.Owner != owner || generation.Status != AIResumeGenerationStatus.Reserved || generation.LeaseUntilUtc <= Now)
                    throw new ConflictException("Generation reservation is no longer valid.", "generation_lease_lost");
                var wallet = await repository.WalletAsync(userId, ct) ?? throw new InvalidOperationException("Credit wallet is missing.");
                var previous = await repository.LatestAsync(userId, sessionId, ct);
                var resume = new TailoredResume
                {
                    UserId = userId, SessionId = sessionId, GenerationId = generation.Id,
                    Version = (previous?.Version ?? 0) + 1, ContentJson = tailoring is null ? JsonSerializer.Serialize(generated.Content, Json) : JsonSerializer.Serialize(tailoring, Json),
                    TemplateCode = tailoring?.MasterDocument.Extension == ".docx" ? "OriginalDocument" : "Professional", GenerationModel = generated.Model,
                    InputTokens = generated.InputTokens, OutputTokens = generated.OutputTokens, GeneratedAtUtc = Now
                };
                repository.Add(resume); generation.Status = AIResumeGenerationStatus.Consumed; generation.LeaseUntilUtc = Now;
                wallet.Reserved--; wallet.LifetimeConsumed++;
                AddLedger(wallet, userId, null, generation.Id, AIResumeCreditKind.Consumption, 0, -1,
                    $"generation:{generation.Id}:consume:{generation.Attempt}");
                current.Status = AIResumeSessionStatus.Generated;
                return resume;
            }, ct);
            return ToResume(result);
        }
        catch (OperationCanceledException) { await ReleaseGeneration(userId, request.IdempotencyKey, owner, CancellationToken.None); if (stagedArtifact is not null) await DeleteUnpersistedArtifact(userId, reservation.Generation.Id, stagedArtifact.StorageKey); throw; }
        catch (Exception ex)
        {
            await ReleaseGeneration(userId, request.IdempotencyKey, owner, CancellationToken.None);
            if (stagedArtifact is not null) await DeleteUnpersistedArtifact(userId, reservation.Generation.Id, stagedArtifact.StorageKey);
            if (ex is AIResumeProviderException providerError)
                throw new AppException("Resume generation could not be completed. Your credit was restored.", 503, providerError.Code);
            throw;
        }
    }

    public async Task<AIResumeResponse> GetResumeAsync(Guid userId, Guid resumeId, CancellationToken ct) =>
        ToResume(await repository.ResumeAsync(userId, resumeId, ct) ?? throw new NotFoundException("Resume was not found."));

    public async Task<IReadOnlyList<AIResumeHistoryItem>> HistoryAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw Invalid("Invalid page range.", "invalid_page");
        var skip = (page - 1) * pageSize;
        var rows = await repository.HistoryAsync(userId, skip, pageSize, ct);
        return rows.Select(x => new AIResumeHistoryItem(x.Resume.Id, x.Session.JobTitle, x.Session.CompanyName,
            x.Session.SourceType, x.Resume.Version, x.Resume.TemplateCode, x.Resume.CreatedAtUtc,
            x.Resume.UpdatedAtUtc, ReadTailoring(x.Resume.ContentJson) is { } tailored && masterDocuments is not null
                ? masterDocuments.Capabilities(tailored.MasterDocument).DownloadFormats : ["pdf", "docx", "txt"])).ToArray();
    }

    public async Task<AIResumeResponse> EditAsync(Guid userId, Guid resumeId, AIResumeEditRequest request, CancellationToken ct)
    {
        if (request.Content is null || !AIResumeContentGuard.WellFormed(request.Content)) throw Invalid("Resume content is invalid.", "invalid_resume_content");
        var currentResume = await repository.ResumeAsync(userId, resumeId, ct) ?? throw new NotFoundException("Resume was not found.");
        if (ReadTailoring(currentResume.ContentJson) is { } currentTailoring)
        {
            var session = await RequiredSession(userId, currentResume.SessionId, ct);
            var source = Deserialize<TailoredResumeContent>(session.SourceJson);
            if (!CompleteIdentityMatch(source, request.Content)) throw Invalid("Only existing text targets may be edited.", "immutable_resume_fact");
            var changes = ResumePatchGuard.Targets(source).Select(target =>
            {
                var text = TargetText(request.Content, target);
                var previous = currentTailoring.Decisions.FirstOrDefault(x => x.Proposal.TargetId == target.Id);
                var proposal = previous?.Proposal ?? new(target.Id, target.Text, text, [target.Id], [], "Candidate edit");
                return new ResumePatchDecision(proposal, text == target.Text ? "original" : "edited", null, null) { EditedText = text };
            }).ToArray();
            return await PersistReview(userId, currentResume, currentTailoring with { Decisions = changes }, request.ExpectedRevision, source, session.JobDescription, ct);
        }
        var updated = await repository.WriteAsync(userId, async () =>
        {
            var resume = await repository.ResumeAsync(userId, resumeId, ct) ?? throw new NotFoundException("Resume was not found.");
            if (resume.EditRevision != request.ExpectedRevision) throw new ConflictException("Resume was changed elsewhere; reload before editing.", "resume_revision_conflict");
            var original = Deserialize<TailoredResumeContent>(resume.ContentJson);
            if (JsonSerializer.Serialize(original.Contact, Json) != JsonSerializer.Serialize(request.Content.Contact, Json) ||
                !ImmutableFactsMatch(original, request.Content)) throw Invalid("Resume identity and employment facts cannot be changed here.", "immutable_resume_fact");
            var session = await RequiredSession(userId, resume.SessionId, ct);
            if (!AIResumeContentGuard.Validate(Deserialize<TailoredResumeContent>(session.SourceJson), request.Content).IsValid)
                throw Invalid("Edited text is not supported by the source resume.", "unsupported_claims");
            var nextRevision = resume.EditRevision + 1;
            repository.Add(new TailoredResumeEdit { ResumeId = resume.Id, Revision = nextRevision, ContentJson = resume.ContentJson });
            resume.ContentJson = JsonSerializer.Serialize(request.Content, Json); resume.EditRevision = nextRevision;
            return resume;
        }, ct);
        return ToResume(updated);
    }

    public async Task<AIResumeDownload> DownloadAsync(Guid userId, Guid resumeId, string format, CancellationToken ct)
    {
        var resume = await repository.ResumeAsync(userId, resumeId, ct) ?? throw new NotFoundException("Resume was not found.");
        try
        {
            if (ReadTailoring(resume.ContentJson) is { } tailoring)
                return await (masterDocuments ?? throw new InvalidDataException("Document service is unavailable.")).DownloadAsync(userId, tailoring, format, ct);
            return renderer.Render(Deserialize<TailoredResumeContent>(resume.ContentJson), format == "original" ? "pdf" : format);
        }
        catch (ArgumentException) { throw Invalid("Choose PDF, DOCX, or TXT.", "invalid_download_format"); }
        catch (InvalidDataException) { throw new AppException("The stored resume document is unavailable.", 503, "resume_artifact_unavailable"); }
    }

    public async Task<AIResumeResponse> ReviewReplacementAsync(Guid userId, Guid resumeId, string targetId, AIResumeReplacementReviewRequest request, CancellationToken ct)
    {
        var resume = await repository.ResumeAsync(userId, resumeId, ct) ?? throw new NotFoundException("Resume was not found.");
        var stored = ReadTailoring(resume.ContentJson) ?? throw Invalid("This resume does not use the patch review contract.", "legacy_resume_review");
        var session = await RequiredSession(userId, resume.SessionId, ct);
        var source = Deserialize<TailoredResumeContent>(session.SourceJson);
        var target = ResumePatchGuard.Targets(source).SingleOrDefault(x => x.Id == targetId)
            ?? throw Invalid("This is not an editable source text target.", "invalid_replacement_target");
        var previous = stored.Decisions.FirstOrDefault(x => x.Proposal.TargetId == targetId);
        var proposal = previous?.Proposal ?? new(target.Id, target.Text, target.Text, [target.Id], [], "Original source text");
        var decision = request.Action switch
        {
            "accept" when previous is not null => new ResumePatchDecision(proposal, "accepted", null, null),
            "original" or "reset" => new ResumePatchDecision(proposal, "original", previous?.RejectionCategory, previous?.RejectionReason),
            "edit" when request.ReplacementText is not null => new ResumePatchDecision(proposal, "edited", null, null) { EditedText = request.ReplacementText },
            _ => throw Invalid("Choose accept, original, edit, or reset.", "invalid_review_action")
        };
        var decisions = stored.Decisions.Where(x => x.Proposal.TargetId != targetId).Append(decision).ToArray();
        return await PersistReview(userId, resume, stored with { Decisions = decisions }, request.ExpectedRevision, source, session.JobDescription, ct);
    }

    private async Task<AIResumeResponse> PersistReview(Guid userId, TailoredResume resume, StoredTailoring stored,
        int expectedRevision, TailoredResumeContent source, string jd, CancellationToken ct)
    {
        if (resume.EditRevision != expectedRevision) throw new ConflictException("Resume was changed elsewhere; reload before editing.", "resume_revision_conflict");
        var documents = masterDocuments ?? throw Invalid("Document editing is unavailable.", "document_service_unavailable");
        var proposals = stored.Decisions.Where(x => x.Status is "accepted" or "edited").Select(x => x.Status == "edited"
            ? x.Proposal with { OriginalText = ResumePatchGuard.Targets(source).Single(t => t.Id == x.Proposal.TargetId).Text,
                ReplacementText = x.EditedText ?? "", SourceEvidenceIds = [x.Proposal.TargetId], MatchedJdTerms = [], Reason = "Candidate edit" }
            : x.Proposal).ToArray();
        var editable = ResumePatchGuard.Targets(source).Where(x => stored.MasterDocument.Extension != ".docx" || stored.MasterDocument.Bindings.Any(b => b.TargetId == x.Id)).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var applied = ResumePatchGuard.Apply(source, new(proposals, stored.EmphasizedSkillEvidenceIds), jd, editable);
        if (applied.Decisions.Any(x => x.Status == "rejected")) throw Invalid("Edited text is not supported by the source resume.", "unsupported_claims");
        var artifact = await documents.CreateArtifactAsync(userId, stored.MasterDocument, source, applied, ct);
        try
        {
            var result = await repository.WriteAsync(userId, async () =>
            {
                var current = await repository.ResumeAsync(userId, resume.Id, ct) ?? throw new NotFoundException("Resume was not found.");
                if (current.EditRevision != expectedRevision) throw new ConflictException("Resume was changed elsewhere; reload before editing.", "resume_revision_conflict");
                repository.Add(new TailoredResumeEdit { ResumeId = current.Id, Revision = expectedRevision + 1, ContentJson = current.ContentJson });
                current.ContentJson = JsonSerializer.Serialize(stored with { Content = applied.Content, Artifact = artifact }, Json);
                current.EditRevision++;
                return current;
            }, ct);
            return ToResume(result);
        }
        catch
        {
            // Retain an artifact if an ambiguous commit actually associated it with the resume.
            try
            {
                var persisted = await repository.ResumeAsync(userId, resume.Id, CancellationToken.None);
                if (ReadTailoring(persisted?.ContentJson ?? "{}")?.Artifact.StorageKey != artifact.StorageKey) await DeleteDocument(userId, artifact.StorageKey);
            }
            catch { /* Storage cleanup cannot hide the original persistence failure. */ }
            throw;
        }
    }

    private static bool CompleteIdentityMatch(TailoredResumeContent source, TailoredResumeContent edited) =>
        JsonSerializer.Serialize(source.Contact, Json) == JsonSerializer.Serialize(edited.Contact, Json) &&
        ImmutableFactsMatch(source, edited) && source.Skills.SequenceEqual(edited.Skills) &&
        source.Experience.Select(x => x.Bullets.Length).SequenceEqual(edited.Experience.Select(x => x.Bullets.Length)) &&
        source.Projects.Select(x => x.Bullets.Length).SequenceEqual(edited.Projects.Select(x => x.Bullets.Length)) &&
        source.Projects.Select(x => string.Join('\0', x.Technologies)).SequenceEqual(edited.Projects.Select(x => string.Join('\0', x.Technologies))) &&
        source.AdditionalInfo.Length == edited.AdditionalInfo.Length && (!string.IsNullOrWhiteSpace(source.ProfessionalSummary) || edited.ProfessionalSummary == source.ProfessionalSummary);
    private static string TargetText(TailoredResumeContent content, ResumeSourceEvidence target)
    {
        var index = int.Parse(target.Id[^3..], CultureInfo.InvariantCulture) - 1;
        if (target.Scope == "summary") return content.ProfessionalSummary;
        if (target.Scope == "additional") return content.AdditionalInfo[index];
        var entry = int.Parse(target.Scope.Split('/')[1], CultureInfo.InvariantCulture);
        return target.Scope.StartsWith("experience/", StringComparison.Ordinal) ? content.Experience[entry].Bullets[index] : content.Projects[entry].Bullets[index];
    }

    private async Task VerifyAndApplyPurchase(AIResumePurchase purchase, CancellationToken ct)
    {
        var verified = await phonePe.GetOrderStatusAsync(purchase.MerchantOrderId, ct);
        if (!string.Equals(verified.MerchantOrderId, purchase.MerchantOrderId, StringComparison.Ordinal) ||
            verified.AmountInMinorUnits != MinorUnits(purchase.Amount))
            throw new ConflictException("PhonePe payment details do not match the resume purchase.", "payment_mismatch");
        await repository.WriteAsync(purchase.UserId, async () =>
        {
            var current = await repository.PurchaseAsync(purchase.MerchantOrderId, ct)
                ?? throw new NotFoundException("Resume purchase was not found.");
            if (current.Status == PaymentStatus.Paid) return true;
            switch (verified.State)
            {
                case PhonePeOrderStateKind.Completed:
                    if (string.IsNullOrWhiteSpace(verified.TransactionId))
                        throw new ConflictException("PhonePe payment is missing its completed transaction reference.", "payment_mismatch");
                    var wallet = await repository.WalletAsync(current.UserId, ct);
                    if (wallet is null) { wallet = new AIResumeCreditWallet { UserId = current.UserId }; repository.Add(wallet); }
                    wallet.Balance = checked(wallet.Balance + current.Credits);
                    wallet.LifetimePurchased = checked(wallet.LifetimePurchased + current.Credits);
                    current.Status = PaymentStatus.Paid; current.ProviderPaymentId = verified.TransactionId; current.PaidAtUtc = Now;
                    AddLedger(wallet, current.UserId, current.Id, null, AIResumeCreditKind.Purchase,
                        current.Credits, 0, $"purchase:{current.Id:N}");
                    break;
                case PhonePeOrderStateKind.Failed: current.Status = PaymentStatus.Failed; break;
                case PhonePeOrderStateKind.Cancelled: current.Status = PaymentStatus.Cancelled; break;
                default: if (current.Status is PaymentStatus.Created) current.Status = PaymentStatus.Pending; break;
            }
            return true;
        }, ct);
    }

    private async Task ReleaseAnalysisLease(Guid userId, Guid sessionId, Guid owner, CancellationToken ct) =>
        await repository.WriteAsync(userId, async () =>
        {
            var session = await RequiredSession(userId, sessionId, ct);
            if (session.Status == AIResumeSessionStatus.Analyzing && session.AnalysisOwner == owner)
            { session.Status = AIResumeSessionStatus.Created; session.AnalysisOwner = null; session.AnalysisLeaseUntilUtc = null; }
            return true;
        }, ct);

    private async Task ReleaseGeneration(Guid userId, Guid key, Guid owner, CancellationToken ct) =>
        await repository.WriteAsync(userId, async () =>
        {
            var generation = await repository.GenerationByKeyAsync(userId, key, ct);
            if (generation is null || generation.Status != AIResumeGenerationStatus.Reserved || generation.Owner != owner) return true;
            var wallet = await repository.WalletAsync(userId, ct) ?? throw new InvalidOperationException("Credit wallet is missing.");
            wallet.Reserved--; wallet.Balance++; generation.Status = AIResumeGenerationStatus.Released;
            generation.FailureCode = "provider_or_validation_failed"; generation.LeaseUntilUtc = Now;
            AddLedger(wallet, userId, null, generation.Id, AIResumeCreditKind.Release, 1, -1,
                $"generation:{generation.Id}:release:{generation.Attempt}");
            var session = await RequiredSession(userId, generation.SessionId, ct);
            session.Status = await repository.LatestAsync(userId, session.Id, ct) is null
                ? AIResumeSessionStatus.Analyzed : AIResumeSessionStatus.Generated;
            return true;
        }, ct);

    private void AddLedger(AIResumeCreditWallet wallet, Guid userId, Guid? purchaseId, Guid? generationId,
        AIResumeCreditKind kind, int availableDelta, int reservedDelta, string key) =>
        repository.Add(new AIResumeCreditTransaction
        {
            UserId = userId, PurchaseId = purchaseId, GenerationId = generationId, Kind = kind,
            AvailableDelta = availableDelta, ReservedDelta = reservedDelta,
            BalanceAfter = wallet.Balance, ReservedAfter = wallet.Reserved, IdempotencyKey = key
        });

    private async Task<AIResumeSession> RequiredSession(Guid userId, Guid sessionId, CancellationToken ct) =>
        await repository.SessionAsync(userId, sessionId, ct) ?? throw new NotFoundException("Resume session was not found.");
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)
        ?? throw new InvalidDataException("Stored resume content is invalid.");
    private static TailoredResumeContent AnalysisSource(TailoredResumeContent source) => source with
    {
        Contact = source.Contact with { Name = "Candidate", Email = "", Phone = "", Links = [] }
    };
    private static string SafeSnapshot(string? value, int max) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static BadRequestException Invalid(string message, string code) => new(message, code);
    private static BadRequestException ReuploadRequired() => new(
        "Please upload your resume again before using AI Resume. Your existing resume was uploaded before document-preserving AI Resume support was enabled.",
        "resume_source_reupload_required");
    private static long MinorUnits(decimal amount) => checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    private static AIResumeCreditResponse Wallet(AIResumeCreditWallet wallet) => new(wallet.Balance, wallet.Reserved, wallet.LifetimePurchased, wallet.LifetimeConsumed);
    private static AIResumeCheckout ToCheckout(AIResumePurchase purchase) => new(purchase.Id, purchase.MerchantOrderId,
        purchase.RedirectUrl, purchase.Amount, purchase.CurrencyCode, purchase.Credits, purchase.PackageCode, purchase.Status,
        purchase.Status == PaymentStatus.Paid ? ReturnPath : null);
    private AIResumeSessionResponse ToSession(AIResumeSession session, TailoredResume? resume) => new(session.Id,
        session.SourceResumeId, session.JobId, session.SourceType, session.JobTitle, session.CompanyName,
        session.Status, session.AnalysisJson is null ? null : Deserialize<ResumeAnalysis>(session.AnalysisJson), resume?.Id)
        { DocumentCapabilities = SessionMaster(session) is { } master ? masterDocuments?.Capabilities(master) : null };
    private AIResumeResponse ToResume(TailoredResume resume)
    {
        var stored = ReadTailoring(resume.ContentJson);
        return new(resume.Id, resume.SessionId, resume.Version, resume.EditRevision, resume.TemplateCode,
            resume.GeneratedAtUtc, resume.UpdatedAtUtc, stored?.Content ?? Deserialize<TailoredResumeContent>(resume.ContentJson))
        { Tailoring = stored is null ? null : new(stored.Decisions, stored.EmphasizedSkillEvidenceIds),
            DocumentCapabilities = stored is null ? null : masterDocuments?.Capabilities(stored.MasterDocument) };
    }
    private static StoredTailoring? ReadTailoring(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("artifact", out _) ? Deserialize<StoredTailoring>(json) : null;
    }
    private static ResumeMasterDocument? SessionMaster(AIResumeSession session)
    {
        using var document = JsonDocument.Parse(session.EvidenceJson);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("masterDocument", out _)
            ? Deserialize<ResumeSessionEvidence>(session.EvidenceJson).MasterDocument : null;
    }
    private void LogRejectedPatch(ResumeGroundingDiagnostic diagnostic)
    {
        if (environment?.IsDevelopment() != true || logger is null) return;
        var metadata = JsonSerializer.Serialize(diagnostic);
        foreach (var credential in new[] { Settings.ApiKey, Settings.WorkspaceId }.OrderByDescending(x => x?.Length ?? 0))
            if (!string.IsNullOrEmpty(credential)) metadata = metadata.Replace(credential, "[credential redacted]", StringComparison.Ordinal);
        LogPatchRejected(logger, metadata, null);
    }
    private async Task DeleteDocument(Guid userId, string key)
    {
        if (masterDocuments is null) return;
        try { await masterDocuments.DeleteAsync(userId, key, CancellationToken.None); }
        catch { /* An orphan must not prevent credit release or overwrite the original failure. */ }
    }
    private async Task DeleteUnpersistedArtifact(Guid userId, Guid generationId, string key)
    {
        try
        {
            var persisted = await repository.ResumeForGenerationAsync(userId, generationId, CancellationToken.None);
            if (ReadTailoring(persisted?.ContentJson ?? "{}")?.Artifact.StorageKey != key) await DeleteDocument(userId, key);
        }
        catch { /* An ambiguous commit must never delete a successfully persisted document. */ }
    }
    private static bool ImmutableFactsMatch(TailoredResumeContent original, TailoredResumeContent edited) =>
        original.Experience.Select(x => (x.Employer, x.Role, x.StartDate, x.EndDate)).SequenceEqual(edited.Experience.Select(x => (x.Employer, x.Role, x.StartDate, x.EndDate))) &&
        original.Education.SequenceEqual(edited.Education) && original.Certifications.SequenceEqual(edited.Certifications) &&
        original.Projects.Select(x => x.Name).SequenceEqual(edited.Projects.Select(x => x.Name));
}
