using System.Net.Mail;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPortal.Persistence.Repositories;

public sealed partial class HiringOutreachService(JobPortalDbContext db,IOptions<HiringOutreachOptions> options,
    IHiringContactProvider contacts,IHiringMailboxProvider google,IHiringCredentialProtector protector,
    IResumeStorage resumes,IResumeTextExtractor resumeText,IAuditWriter audit,IAIApplyLanguageModel ai,TimeProvider clock,ILogger<HiringOutreachService> logger) : IHiringOutreachService
{
    private static readonly SemaphoreSlim TestGate=new(1,1);
    private const long LockKey=0x484952494E474D41;
    private HiringOutreachOptions Limits=>options.Value;
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private IQueryable<HiringOutreachSession> Sessions=>db.Set<HiringOutreachSession>();
    private IQueryable<HiringOutreachSend> Sends=>db.Set<HiringOutreachSend>();
    [LoggerMessage(Level=LogLevel.Information,Message="Hiring outreach {Operation} session {SessionId} user {ActorId} job {JobId} provider {Provider} result {State} count {Count} durationMs {ElapsedMs}")]
    private static partial void Outcome(ILogger logger,string operation,Guid sessionId,Guid actorId,Guid jobId,string provider,string state,int count,double elapsedMs);
    private async Task<User> Candidate(Guid actor,CancellationToken ct)
    {
        if(!Limits.Enabled) throw new AppException("Hiring-team outreach is unavailable.",503,"outreach_unavailable");
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==actor && x.Status==UserStatus.Active && x.Role.Name=="Candidate",ct)
            ?? throw new UnauthorizedException();
    }
    private Task Audit(Guid actor,Guid id,string operation,CancellationToken ct)=>audit.AppendAsync(new(AuditAction.Update,"HiringOutreach",id.ToString(),
        new Dictionary<string,string?> { ["operation"]=operation },new(actor,"Candidate")),ct);
    private async Task<T> Write<T>(Func<Task<T>> action,CancellationToken ct)
    {
        if(!db.Database.IsRelational())
        {
            await TestGate.WaitAsync(ct);
            try { var value=await action(); await db.SaveChangesAsync(ct); return value; }
            finally { TestGate.Release(); }
        }
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async ()=>
        {
            db.ChangeTracker.Clear();
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockKey})",ct);
            var value=await action(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return value;
        });
    }
    private async Task<HiringOutreachSession> Owned(Guid actor,Guid id,CancellationToken ct) =>
        await Sessions.SingleOrDefaultAsync(x=>x.CandidateId==actor && x.Id==id,ct) ?? throw new NotFoundException("Outreach session not found.");
    private async Task<int> Remaining(Guid actor,CancellationToken ct)
    {
        var memberships=await db.Memberships.AsNoTracking().Where(x=>x.UserId==actor && x.Status==MembershipStatus.Active && x.StartsAtUtc<=Now && (x.EndsAtUtc==null || x.EndsAtUtc>Now))
            .Select(x=>x.PlanCode).ToArrayAsync(ct);
        var paid=memberships.Select(x=>Limits.PlanMonthlyDiscoveries.GetValueOrDefault(x)).DefaultIfEmpty(0).Max();
        var month=new DateTime(Now.Year,Now.Month,1,0,0,0,DateTimeKind.Utc); var day=Now.Date;
        var usage=db.Set<HiringOutreachSession>().IgnoreQueryFilters().Where(x=>x.CandidateId==actor && x.DispatchedAtUtc!=null && x.UsageState!="Released");
        var total=await usage.CountAsync(ct); var monthly=await usage.CountAsync(x=>x.DispatchedAtUtc>=month,ct);
        var daily=await usage.CountAsync(x=>x.DispatchedAtUtc>=day,ct);
        return Math.Max(0,Math.Min(Limits.DailyDiscoveries-daily,Math.Max(Limits.PromotionalLifetimeDiscoveries-total,paid-monthly)));
    }
    public async Task<HiringSession> DiscoverAsync(Guid actor,DiscoverHiringTeamRequest request,CancellationToken ct)
    {
        var started=Stopwatch.GetTimestamp();
        await Candidate(actor,ct);
        if(request.RequestKey==Guid.Empty || request.JobId==Guid.Empty) throw new BadRequestException("Job and request key are required.");
        var plan=await Write(async ()=>
        {
            var replay=await db.Set<HiringOutreachDiscoveryRequest>().SingleOrDefaultAsync(x=>x.CandidateId==actor && x.RequestKey==request.RequestKey,ct);
            if(replay is not null) { if(replay.JobId!=request.JobId) throw new ConflictException("Request key belongs to another job."); return (Id:replay.SessionId,false); }
            var cached=await Sessions.Where(x=>x.CandidateId==actor && x.JobId==request.JobId && (x.ExpiresAtUtc>Now &&
                (x.State=="Found" || x.State=="NoContactsFound" || x.State=="Sent" || x.State=="PartiallySent" || x.State=="Sending" || x.State=="SendingUnresolved") || x.State=="Searching"))
                .OrderByDescending(x=>x.CreatedAtUtc).FirstOrDefaultAsync(ct);
            if(cached is not null)
            { db.Add(new HiringOutreachDiscoveryRequest { CandidateId=actor,JobId=request.JobId,SessionId=cached.Id,RequestKey=request.RequestKey }); return (cached.Id,false); }
            var job=await PublicJobRepository.AvailableJobs(db.Jobs.AsNoTracking(),Now).Include(x=>x.Company).SingleOrDefaultAsync(x=>x.Id==request.JobId,ct)
                ?? throw new NotFoundException("Available job not found.");
            var domain=Domain(job.Company.NormalizedWebsiteDomain ?? job.Company.WebsiteUrl);
            var s=new HiringOutreachSession { CandidateId=actor,JobId=job.Id,CompanyId=job.CompanyId,RequestKey=request.RequestKey,
                CompanyName=Bound(job.Company.Name,300),CompanyDomain=domain,JobTitle=Bound(job.Title,300),JobDescription=Bound(string.Join('\n',new[]{job.Description,job.Requirements,job.Responsibilities}.Where(x=>!string.IsNullOrWhiteSpace(x))),20000),
                Location=Bound(job.Location ?? "",500),Department=Bound(job.Department ?? "",200),Provider=contacts.Name,ExpiresAtUtc=Now.AddDays(Limits.CacheDays) };
            if(domain.Length==0) { s.State="NoContactsFound"; s.FailureCode="CompanyDomainUnresolved"; }
            else if(!contacts.IsAvailable) { s.State="ProviderUnavailable"; s.FailureCode="ContactProviderUnavailable"; }
            else if(await Remaining(actor,ct)==0) { s.State="QuotaExceeded"; s.FailureCode="DiscoveryQuotaExceeded"; }
            else
            {
                var previous=await db.Set<HiringOutreachSession>().IgnoreQueryFilters().CountAsync(x=>x.CandidateId==actor && x.DispatchedAtUtc!=null && x.UsageState!="Released",ct);
                var plans=await db.Memberships.AsNoTracking().Where(x=>x.UserId==actor && x.Status==MembershipStatus.Active && x.StartsAtUtc<=Now && (x.EndsAtUtc==null || x.EndsAtUtc>Now)).Select(x=>x.PlanCode).ToArrayAsync(ct);
                s.AllowanceSource=previous<Limits.PromotionalLifetimeDiscoveries ? "Promotion" : plans.OrderByDescending(x=>Limits.PlanMonthlyDiscoveries.GetValueOrDefault(x)).First(x=>Limits.PlanMonthlyDiscoveries.GetValueOrDefault(x)>0);
                s.DispatchedAtUtc=Now; s.UsageState="Reserved";
            }
            db.Add(s); await Audit(actor,s.Id,s.DispatchedAtUtc.HasValue ? "DiscoveryReservedAndDispatched" : s.State,ct);
            db.Add(new HiringOutreachDiscoveryRequest { CandidateId=actor,JobId=request.JobId,SessionId=s.Id,RequestKey=request.RequestKey });
            return (s.Id,s.DispatchedAtUtc.HasValue);
        },ct);
        if(plan.Item2)
        {
            var session=await Owned(actor,plan.Id,ct); IReadOnlyList<HiringProviderContact> found=[]; string? error=null;
            try { found=await contacts.DiscoverAsync(new(session.CompanyName,session.CompanyDomain,session.JobTitle,session.Department),ct); }
            catch(HiringProviderException ex) { error=ex.Code; }
            catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or JsonException) { error="DiscoveryOutcomeUnknown"; }
            var ranked=HiringContactPolicy.Select(found,session.CompanyDomain,session.JobTitle,session.Department,Limits);
            await Write(async ()=>
            {
                var current=await Owned(actor,plan.Id,CancellationToken.None);
                if(current.State!="Searching") return false;
                var rank=0;
                foreach(var item in ranked) db.Add(new HiringOutreachContact { SessionId=current.Id,Rank=rank++,FullName=item.FullName,FirstName=item.FirstName,Email=item.Email,
                    Title=item.Title,Department=item.Department,Type=HiringContactPolicy.Type(item.Title),Verification=item.Verification,Confidence=item.Confidence,
                    LinkedInUrl=item.LinkedInUrl,ProviderReference=item.Reference,VerifiedAtUtc=Now });
                current.State=error is null ? ranked.Count>0 ? "Found" : "NoContactsFound" : "ProviderUnavailable";
                current.FailureCode=error; current.Revision=Guid.NewGuid(); await Audit(actor,current.Id,current.State,CancellationToken.None);
                current.UsageState=error is null ? "Consumed" : error is "ContactProviderConfiguration" or "ContactProviderRateLimited" ? "Released" : "Uncertain";
                await Audit(actor,current.Id,"DiscoveryUsage:"+current.UsageState,CancellationToken.None);
                var elapsed=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Outcome(logger,"discovery",current.Id,actor,current.JobId,contacts.Name,current.State,ranked.Count,elapsed); return true;
            },CancellationToken.None);
        }
        var response=await GetAsync(actor,plan.Id,CancellationToken.None);
        return response with { Usage=response.Usage with { Charged=plan.Item2 && response.Usage.Charged } };
    }
    public async Task<HiringSession> GetAsync(Guid actor,Guid sessionId,CancellationToken ct)
    {
        await Candidate(actor,ct); var s=await Owned(actor,sessionId,ct);
        var found=await ContactDtos(sessionId,ct);
        var stale=s.State=="Searching" && s.DispatchedAtUtc<Now.AddMinutes(-2);
        var state=stale ? "ProviderUnavailable" : s.State=="Sending" && s.UpdatedAtUtc<Now.AddMinutes(-2) ? "SendingUnresolved" : s.State;
        return new(s.Id,state,s.JobId,s.JobTitle,s.CompanyName,s.CompanyDomain,found,await MailboxAsync(actor,ct),new(s.DispatchedAtUtc.HasValue && s.UsageState!="Released",await Remaining(actor,ct)),stale ? "DiscoveryOutcomeUnknown" : s.FailureCode);
    }
    public async Task<IReadOnlyList<HiringSession>> HistoryAsync(Guid actor,int page,int pageSize,CancellationToken ct)
    {
        await Candidate(actor,ct); if(page is <1 or >10000 || pageSize is <1 or >50) throw new BadRequestException("Invalid page.");
        var ids=await Sessions.Where(x=>x.CandidateId==actor).OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).Select(x=>x.Id).ToArrayAsync(ct);
        var result=new List<HiringSession>(); foreach(var id in ids) result.Add(await GetAsync(actor,id,ct)); return result;
    }
    private async Task<HiringContact[]> ContactDtos(Guid id,CancellationToken ct)=>await db.Set<HiringOutreachContact>().AsNoTracking().Where(x=>x.SessionId==id).OrderBy(x=>x.Rank).ThenBy(x=>x.Id)
        .Select(x=>new HiringContact(x.Id,x.FullName,x.Title,x.Department,x.Type,x.Email,x.Verification,x.Confidence,x.LinkedInUrl,x.Type=="Recruiter" || x.Type=="TalentAcquisition")).ToArrayAsync(ct);
    public async Task<IReadOnlyList<HiringResume>> ResumesAsync(Guid actor,CancellationToken ct)
    {
        var user=await Candidate(actor,ct);
        if(!string.IsNullOrWhiteSpace(user.ResumeStorageKey) && !await db.ResumeDocumentBlobs.AnyAsync(x=>x.OwnerUserId==actor && x.StorageKey==user.ResumeStorageKey,ct))
        {
            // Reuse the storage adapter's existing owner-checked legacy-to-blob recovery.
            await using var legacy=await resumes.OpenReadAsync(actor,user.ResumeStorageKey,originalFileName:user.ResumeFileName,contentType:user.ResumeContentType,cancellationToken:ct);
        }
        return await db.ResumeDocumentBlobs.AsNoTracking().Where(x=>x.OwnerUserId==actor).OrderByDescending(x=>x.CreatedAtUtc).Take(100)
            .Select(x=>new HiringResume(x.Id,x.OriginalFileName,x.StorageKey==user.ResumeStorageKey)).ToArrayAsync(ct);
    }
    private async Task<ResumeDocumentBlob> Resume(Guid actor,Guid id,CancellationToken ct)=>
        await db.ResumeDocumentBlobs.AsNoTracking().Where(x=>x.OwnerUserId==actor && x.Id==id)
            .Select(x=>new ResumeDocumentBlob { Id=x.Id,OwnerUserId=x.OwnerUserId,ResumeId=x.ResumeId,StorageKey=x.StorageKey,OriginalFileName=x.OriginalFileName,Extension=x.Extension,ContentType=x.ContentType,FileLength=x.FileLength,Sha256=x.Sha256 })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Candidate resume not found.");
    private void Recipients(IReadOnlyList<Guid> ids)
    { if(ids.Count is <1 || ids.Count>Limits.MaximumContacts || ids.Distinct().Count()!=ids.Count) throw new BadRequestException("Select a bounded set of distinct session contacts."); }
    public async Task<HiringDraft> DraftAsync(Guid actor,Guid sessionId,HiringDraftRequest request,CancellationToken ct)
    {
        var started=Stopwatch.GetTimestamp();
        var user=await Candidate(actor,ct); var s=await Owned(actor,sessionId,ct); Recipients(request.RecipientIds);
        var selected=(await ContactDtos(sessionId,ct)).Where(x=>request.RecipientIds.Contains(x.Id)).ToArray();
        if(selected.Length!=request.RecipientIds.Count || s.ExpiresAtUtc<=Now) throw new BadRequestException("Discovery expired or recipient is not in this session.");
        var resume=await Resume(actor,request.ResumeId,ct);
        var attachment=await ReadAttachment(actor,resume,ct); var extracted="";
        try
        {
            using var stream=new MemoryStream(attachment);
            extracted=Bound(await resumeText.ExtractAsync(stream,resume.Extension,ct),50000);
        }
        catch(Exception ex) when(ex is InvalidDataException or NotSupportedException or FormatException) { /* Profile facts remain usable; no invented resume claims. */ }
        var skillNames=await db.CandidateSkills.AsNoTracking().Where(x=>x.UserId==actor).Select(x=>x.Name).Take(30).ToArrayAsync(ct);
        var relevant=skillNames.Where(x=>s.JobDescription.Contains(x,StringComparison.OrdinalIgnoreCase) || s.JobTitle.Contains(x,StringComparison.OrdinalIgnoreCase)).Take(5).ToArray();
        var originalContext=(s.JobDescription+" "+s.JobTitle).ToUpperInvariant();
        var jobSkills=await db.Skills.AsNoTracking().Where(x=>x.NormalizedName.Length>1 && originalContext.Contains(x.NormalizedName)).OrderByDescending(x=>x.Name.Length).Take(100).Select(x=>x.Name).ToArrayAsync(ct);
        var resumeSkills=jobSkills.Where(x=>extracted.Contains(x,StringComparison.OrdinalIgnoreCase)).Take(5).ToArray();
        var name=Bound(user.FirstName+" "+user.LastName,200).Trim();
        var sentences=new Dictionary<string,string> {
            ["interest"]=$"I am interested in the {s.JobTitle} position at {s.CompanyName}. I found the opportunity on CareerHarbor and would appreciate the chance to be considered for the role.",
            ["relevance"]=(relevant.Length>0 ? $"My CareerHarbor profile lists {string.Join(", ",relevant)}, which also appear in the job context. " : "")+
                (resumeSkills.Length>0 ? $"My selected resume mentions {string.Join(", ",resumeSkills)}. " : "")+
                (user.YearsOfExperience is >0 and <=80 ? $"My profile records {user.YearsOfExperience.Value.ToString("0.#",CultureInfo.InvariantCulture)} years of experience. " : "")+
                "I would welcome a conversation about the responsibilities described in the job posting and how my background might fit your team's needs.",
            ["resume"]="I have attached my selected resume for your review. It provides further details about my background and experience.",
            ["cta"]="Would you be available for a brief conversation, or able to direct me to the appropriate hiring contact? Thank you for your time and consideration." };
        var order=new[]{"interest","relevance","resume","cta"}; var source="Deterministic";
        if(Limits.AiDraftingEnabled)
        {
            // AI may order approved factual sentences, never introduce unverified candidate claims.
            try
            {
                var generated=await ai.GenerateAsync(new("Return only a JSON array ordering all four supplied sentence ids: interest, relevance, resume, cta. No new prose.",
                    ApplicationQuestionCategory.RoleMotivation,ApplicationAnswerType.LongText,200,sentences),ct);
                var proposed=generated is null ? null : JsonSerializer.Deserialize<string[]>(generated.Answer);
                if(proposed is { Length:4 } && proposed.Distinct().Count()==4 && proposed.All(sentences.ContainsKey)) { order=proposed; source="AIOrderedVerifiedFacts"; }
            }
            catch(Exception ex) when(ex is JsonException or HttpRequestException or OperationCanceledException or HiringProviderException) { ct.ThrowIfCancellationRequested(); }
        }
        var draft=new HiringOutreachDraft { SessionId=sessionId,ResumeId=resume.Id,RecipientIdsJson=JsonSerializer.Serialize(request.RecipientIds.Order()),
            Subject=Bound($"Application for {s.JobTitle} — {name}",200),Body=$"Dear {selected[0].Name.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Hiring team"},\n\n"+
                string.Join("\n\n",order.Select(x=>sentences[x]))+$"\n\nBest regards,\n{name}",Source=source };
        await Write(async ()=> { await Owned(actor,sessionId,ct); db.Add(draft); await Audit(actor,sessionId,"DraftCreated:"+source,ct); return true; },ct);
        var elapsed=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Outcome(logger,"draft",sessionId,actor,s.JobId,"ExistingAIAbstraction",source,selected.Length,elapsed);
        return new(draft.Id,sessionId,await MailboxAsync(actor,ct),selected,draft.Subject,draft.Body,new(resume.Id,resume.OriginalFileName,resume.StorageKey==user.ResumeStorageKey),true,source);
    }
    public async Task<HiringMailbox> MailboxAsync(Guid actor,CancellationToken ct)
    {
        await Candidate(actor,ct); var m=await ActiveMailbox(actor,ct); return new(m is not null,"Google",m?.EmailAddress);
    }
    private Task<CandidateMailboxConnection?> ActiveMailbox(Guid actor,CancellationToken ct)=>db.CandidateMailboxConnections
        .Where(x=>x.CandidateId==actor && x.Provider=="Google" && x.RevokedAtUtc==null && db.Set<HiringMailboxSecret>().Any(s=>s.MailboxId==x.Id && s.ProtectedRefreshToken!=""))
        .OrderByDescending(x=>x.ConnectedAtUtc).FirstOrDefaultAsync(ct);
    public async Task<HiringOAuthStart> ConnectAsync(Guid actor,CancellationToken ct)
    {
        await Candidate(actor,ct); if(!google.IsAvailable) throw new AppException("Gmail connection is unavailable.",503,"gmail_unavailable");
        var state=Random(); var browser=Random(); var challenge=google.Authorize(state);
        await Write(async ()=> { db.Add(new HiringMailboxAuthorization { CandidateId=actor,StateHash=Hash(state),BrowserHash=Hash(browser),
            ProtectedVerifier=protector.Protect(actor,challenge.Verifier),ExpiresAtUtc=Now.AddMinutes(10) }); await Audit(actor,actor,"MailboxAuthorizationStarted",ct); return true; },ct);
        return new(challenge.Url,browser);
    }
    public async Task<HiringMailbox> CallbackAsync(string state,string code,string browserBinding,CancellationToken ct)
    {
        if(state.Length is <20 or >200 || browserBinding.Length is <20 or >200 || code.Length is <1 or >4096) throw new BadRequestException("Invalid mailbox authorization.");
        var auth=await Write(async ()=>
        {
            var hash=Hash(state); var a=await db.Set<HiringMailboxAuthorization>().SingleOrDefaultAsync(x=>x.StateHash==hash,ct);
            if(a is null || a.ConsumedAtUtc.HasValue || a.ExpiresAtUtc<=Now || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.BrowserHash),Encoding.ASCII.GetBytes(Hash(browserBinding))))
                throw new BadRequestException("Invalid or expired mailbox authorization.");
            await Candidate(a.CandidateId,ct); a.ConsumedAtUtc=Now; return (a.CandidateId,a.ProtectedVerifier);
        },ct);
        var grant=await google.ExchangeAsync(code,protector.Unprotect(auth.CandidateId,auth.ProtectedVerifier),ct);
        if(!grant.Scopes.Split(' ').Contains("https://www.googleapis.com/auth/gmail.send") || !MailAddress.TryCreate(grant.Email,out _) || string.IsNullOrWhiteSpace(grant.RefreshToken))
            throw new BadRequestException("Gmail send permission and offline access are required.");
        await Write(async ()=>
        {
            await Candidate(auth.CandidateId,ct);
            var existing=await db.CandidateMailboxConnections.Where(x=>x.CandidateId==auth.CandidateId && x.Provider=="Google").ToArrayAsync(ct);
            foreach(var item in existing) item.RevokedAtUtc=Now;
            var mailbox=existing.SingleOrDefault(x=>x.ProviderAccountIdentifier==grant.AccountId) ?? new CandidateMailboxConnection { CandidateId=auth.CandidateId,Provider="Google",ProviderAccountIdentifier=grant.AccountId };
            if(!existing.Contains(mailbox)) db.Add(mailbox);
            mailbox.EmailAddress=grant.Email; mailbox.GrantedScopes=grant.Scopes; mailbox.ConnectedAtUtc=Now; mailbox.RevokedAtUtc=null;
            var secret=await db.Set<HiringMailboxSecret>().SingleOrDefaultAsync(x=>x.MailboxId==mailbox.Id,ct);
            if(secret is null) { secret=new HiringMailboxSecret { MailboxId=mailbox.Id }; db.Add(secret); }
            secret.ProtectedRefreshToken=protector.Protect(auth.CandidateId,grant.RefreshToken); mailbox.TokenReference=secret.Id.ToString("N");
            foreach(var old in existing.Where(x=>x.Id!=mailbox.Id))
            { var retired=await db.Set<HiringMailboxSecret>().SingleOrDefaultAsync(x=>x.MailboxId==old.Id,ct); if(retired is not null) retired.ProtectedRefreshToken=""; }
            await Audit(auth.CandidateId,mailbox.Id,"MailboxConnected",ct); return true;
        },ct);
        return await MailboxAsync(auth.CandidateId,ct);
    }
    public async Task DisconnectAsync(Guid actor,CancellationToken ct)
    {
        await Candidate(actor,ct);
        var encrypted=await Write(async ()=>
        {
            var m=await ActiveMailbox(actor,ct); if(m is null) return "";
            var secret=await db.Set<HiringMailboxSecret>().SingleAsync(x=>x.MailboxId==m.Id,ct);
            var token=secret.ProtectedRefreshToken; secret.ProtectedRefreshToken=""; m.RevokedAtUtc=Now;
            await Audit(actor,m.Id,"MailboxDisconnected",ct); return token;
        },ct);
        if(encrypted.Length>0)
        { try { await google.RevokeAsync(protector.Unprotect(actor,encrypted),ct); } catch(Exception ex) when(ex is HiringProviderException or HttpRequestException or OperationCanceledException) { ct.ThrowIfCancellationRequested(); } }
    }
    public async Task<HiringSendResponse> SendAsync(Guid actor,Guid sessionId,HiringSendRequest request,CancellationToken ct)
    {
        var started=Stopwatch.GetTimestamp();
        await Candidate(actor,ct); Recipients(request.RecipientIds);
        if(request.RequestKey==Guid.Empty || string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length>200 || request.Subject.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(request.Body) || request.Body.Length>10000 || request.Body.Contains('\0')) throw new BadRequestException("Invalid message or request key.");
        var payload=Hash(JsonSerializer.Serialize(new {request.DraftId,Recipients=request.RecipientIds.Order().ToArray(),request.Subject,request.Body,request.ResumeId}));
        var replay=await Sends.Where(x=>x.CandidateId==actor && x.SessionId==sessionId && x.RequestKey==request.RequestKey).ToArrayAsync(ct);
        if(replay.Length>0) { if(replay.Any(x=>x.PayloadHash!=payload)) throw new ConflictException("Send request key has different content."); return SendDto(replay); }
        var resume=await Resume(actor,request.ResumeId,ct);
        var bytes=await ReadAttachment(actor,resume,ct);
        var reserved=await Write(async ()=>
        {
            await Candidate(actor,ct); var session=await Owned(actor,sessionId,ct);
            var prior=await Sends.Where(x=>x.CandidateId==actor && x.SessionId==sessionId && x.RequestKey==request.RequestKey).ToArrayAsync(ct);
            if(prior.Length>0) { if(prior.Any(x=>x.PayloadHash!=payload)) throw new ConflictException("Send request key has different content."); return (prior,false,Guid.Empty,""); }
            if(session.AllowanceSource!="Promotion" && (Limits.PlanMonthlyDiscoveries.GetValueOrDefault(session.AllowanceSource)<=0 ||
                !await db.Memberships.AnyAsync(x=>x.UserId==actor && x.PlanCode==session.AllowanceSource && x.Status==MembershipStatus.Active && x.StartsAtUtc<=Now && (x.EndsAtUtc==null || x.EndsAtUtc>Now),ct)))
                throw new AppException("Active outreach entitlement required.",403,"OutreachEntitlementRequired");
            var draft=await db.Set<HiringOutreachDraft>().SingleOrDefaultAsync(x=>x.Id==request.DraftId && x.SessionId==sessionId,ct) ?? throw new NotFoundException("Reviewed draft not found.");
            if(draft.ResumeId!=request.ResumeId) throw new ConflictException("Resume changed; generate a new draft for review.");
            await Resume(actor,request.ResumeId,ct);
            if(session.ExpiresAtUtc<=Now) throw new ConflictException("Discovery expired; refresh contacts before sending.");
            var allowed=JsonSerializer.Deserialize<Guid[]>(draft.RecipientIdsJson) ?? [];
            var recipients=await db.Set<HiringOutreachContact>().Where(x=>x.SessionId==sessionId && request.RecipientIds.Contains(x.Id)).ToArrayAsync(ct);
            if(recipients.Length!=request.RecipientIds.Count || request.RecipientIds.Except(allowed).Any() || recipients.Any(x=>!HiringContactPolicy.Verified(x.Verification,x.Confidence,Limits))) throw new BadRequestException("Recipient is not approved for this draft.");
            var mailbox=await ActiveMailbox(actor,ct) ?? throw new AppException("Reconnect Gmail before sending.",409,"ReconnectGmailRequired");
            if(!google.IsAvailable) throw new AppException("Gmail sending is unavailable.",503,"gmail_unavailable");
            var emails=recipients.Select(x=>x.Email).ToArray();
            if(await db.Set<HiringOutreachSend>().IgnoreQueryFilters().AnyAsync(x=>x.CandidateId==actor && x.JobId==session.JobId && emails.Contains(x.EmailSnapshot) && x.State!="Failed",ct))
                throw new ConflictException("This job/contact already has a sent, reserved or unresolved message.");
            var all=db.Set<HiringOutreachSend>().IgnoreQueryFilters().Where(x=>x.CandidateId==actor && x.State!="Failed");
            var month=new DateTime(Now.Year,Now.Month,1,0,0,0,DateTimeKind.Utc); var day=Now.Date;
            if(await all.CountAsync(x=>x.CreatedAtUtc>=day,ct)+recipients.Length>Limits.DailySends || await all.CountAsync(x=>x.CreatedAtUtc>=month,ct)+recipients.Length>Limits.MonthlySends)
                throw new AppException("Outreach send allowance exhausted.",403,"QuotaExceeded");
            var secret=await db.Set<HiringMailboxSecret>().SingleAsync(x=>x.MailboxId==mailbox.Id,ct);
            var sends=recipients.Select(r=>new HiringOutreachSend { CandidateId=actor,SessionId=sessionId,JobId=session.JobId,ContactId=r.Id,DraftId=draft.Id,ResumeId=resume.Id,
                RequestKey=request.RequestKey,PayloadHash=payload,EmailSnapshot=r.Email,Subject=request.Subject,Body=Personalize(request.Body,r.FirstName),State="Reserved" }).ToArray();
            db.AddRange(sends); session.State="Sending"; session.Revision=Guid.NewGuid(); await Audit(actor,sessionId,"SendReserved",ct);
            return (sends,true,mailbox.Id,secret.ProtectedRefreshToken);
        },ct);
        if(!reserved.Item2) return SendDto(reserved.Item1);
        string token;
        try { token=protector.Unprotect(actor,reserved.Item4); }
        catch(CryptographicException) { throw new AppException("Reconnect Gmail before sending.",409,"ReconnectGmailRequired"); }
        foreach(var send in reserved.Item1)
        {
            string? sender=await Write(async ()=>
            {
                var row=await Sends.SingleAsync(x=>x.Id==send.Id,CancellationToken.None);
                var mailbox=await ActiveMailbox(actor,CancellationToken.None);
                if(mailbox is null || mailbox.Id!=reserved.Item3 || ct.IsCancellationRequested)
                { row.State="Failed"; row.FailureCode="ReconnectGmailRequired"; return null; }
                row.State="Sending"; row.DispatchedAtUtc=Now; row.Revision=Guid.NewGuid(); await Audit(actor,row.Id,"GmailDispatchStarted",CancellationToken.None); return mailbox.EmailAddress;
            },CancellationToken.None);
            if(sender is null) continue;
            HiringMailResult result;
            try { result=await google.SendAsync(token,new(sender,send.EmailSnapshot,send.Subject,send.Body,resume.OriginalFileName,resume.ContentType,bytes,send.Id.ToString("N")),ct); }
            catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or HiringProviderException or JsonException) { result=new("Unknown",FailureCode:"GmailOutcomeUnknown"); }
            await Write(async ()=>
            {
                var row=await Sends.SingleAsync(x=>x.Id==send.Id,CancellationToken.None);
                row.State=result.State; row.ProviderMessageId=result.MessageId; row.ProviderThreadId=result.ThreadId; row.FailureCode=result.FailureCode;
                row.SentAtUtc=result.State=="Sent" ? Now : null; row.Revision=Guid.NewGuid();
                var secret=await db.Set<HiringMailboxSecret>().SingleAsync(x=>x.MailboxId==reserved.Item3,CancellationToken.None);
                if(result.State=="Sent") secret.LastUsedAtUtc=Now;
                if(result.FailureCode=="ReconnectGmailRequired") { var m=await db.CandidateMailboxConnections.SingleAsync(x=>x.Id==reserved.Item3,CancellationToken.None); m.RevokedAtUtc=Now; secret.ProtectedRefreshToken=""; }
                await Audit(actor,row.Id,"GmailResult:"+result.State,CancellationToken.None); return true;
            },CancellationToken.None);
        }
        var final=await Sends.AsNoTracking().Where(x=>x.CandidateId==actor && x.SessionId==sessionId && x.RequestKey==request.RequestKey).ToArrayAsync(CancellationToken.None);
        var response=SendDto(final);
        await Write(async ()=> { var s=await Owned(actor,sessionId,CancellationToken.None); s.State=response.State; s.Revision=Guid.NewGuid(); return true; },CancellationToken.None);
        var elapsed=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Outcome(logger,"send",sessionId,actor,final[0].JobId,"Google",response.State,final.Length,elapsed); return response;
    }
    private static HiringSendResponse SendDto(HiringOutreachSend[] rows)=>new(rows.All(x=>x.State=="Sent") ? "Sent" : rows.Any(x=>x.State=="Sent") ? "PartiallySent" :
        rows.Any(x=>x.State is "Sending" or "Reserved" or "Unknown") ? "SendingUnresolved" : "Failed",rows.Select(x=>new HiringSendResult(x.ContactId,x.State,x.ProviderMessageId,x.FailureCode)).ToArray());
    private async Task<byte[]> ReadAttachment(Guid actor,ResumeDocumentBlob resume,CancellationToken ct)
    {
        if(resume.FileLength>Limits.MaximumAttachmentBytes) throw new BadRequestException("Selected resume exceeds attachment limit.");
        await using var content=await resumes.OpenReadAsync(actor,resume.StorageKey,resume.ResumeId,resume.OriginalFileName,resume.ContentType,ct)
            ?? throw new NotFoundException("Resume document is unavailable.");
        using var bytes=new MemoryStream(); var buffer=new byte[8192]; int read;
        while((read=await content.ReadAsync(buffer,ct))>0) { if(bytes.Length+read>Limits.MaximumAttachmentBytes) throw new BadRequestException("Resume exceeds attachment limit."); await bytes.WriteAsync(buffer.AsMemory(0,read),ct); }
        var result=bytes.ToArray();
        if(result.Length==0 || HashBytes(result)!=resume.Sha256) throw new ConflictException("Resume changed; regenerate the draft.");
        return result;
    }
    public async Task<IReadOnlyList<HiringSupportSession>> SupportAsync(Guid actor,int page,int pageSize,CancellationToken ct)
    {
        if(!await db.Users.AnyAsync(x=>x.Id==actor && x.Status==UserStatus.Active && x.Role.Name=="Administrator",ct)) throw new UnauthorizedException();
        if(page is <1 or >10000 || pageSize is <1 or >50) throw new BadRequestException("Invalid support page.");
        return await Sessions.AsNoTracking().OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize)
            .Select(x=>new HiringSupportSession(x.Id,x.CandidateId,x.JobId,x.State,x.Provider,x.UsageState,
                db.Set<HiringOutreachContact>().Count(c=>c.SessionId==x.Id),db.Set<HiringOutreachSend>().Count(s=>s.SessionId==x.Id),
                db.Set<HiringOutreachSend>().Count(s=>s.SessionId==x.Id && s.State=="Sent"),x.CreatedAtUtc,x.FailureCode)).ToArrayAsync(ct);
    }
    private static string Personalize(string body,string firstName)
    {
        var firstLine=body.IndexOf('\n'); var rest=body.StartsWith("Dear ",StringComparison.OrdinalIgnoreCase) && firstLine>=0 ? body[(firstLine+1)..].TrimStart() : body;
        return $"Dear {(string.IsNullOrWhiteSpace(firstName) ? "Hiring team" : firstName)},\n\n{rest}";
    }
    private static string Random()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value)=>HashBytes(Encoding.UTF8.GetBytes(value));
    private static string HashBytes(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static string Bound(string value,int max)=>value.Length>max ? value[..max] : value;
    private static string Domain(string? value)
    {
        if(string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Contains("://",StringComparison.Ordinal) ? value : "https://"+value,UriKind.Absolute,out var uri) ||
            uri.HostNameType!=UriHostNameType.Dns || !uri.Host.Contains('.') || uri.IsLoopback || uri.UserInfo.Length>0) return "";
        var host=uri.IdnHost.ToLowerInvariant(); return host.StartsWith("www.",StringComparison.Ordinal) ? host[4..] : host;
    }
}
