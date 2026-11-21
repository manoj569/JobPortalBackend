using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class AcceleratorService(IAcceleratorStore store, IOptions<AcceleratorOptions> options,
    TimeProvider clock, IAuditWriter audit, IMembershipPlanProvider pricing)
{
    private AcceleratorOptions Settings => options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static readonly HashSet<string> AssessmentMethods = new(StringComparer.Ordinal)
        { "mcq", "scenario", "written", "coding", "project", "interview" };

    public AcceleratorPlanResponse Plan()
    {
        var o = Settings;
        var configured = pricing.GetPlans().SingleOrDefault(x => x.Code == AcceleratorOptions.PlanCode);
        var plan = configured ?? new MembershipPlan(AcceleratorOptions.PlanCode, "CareerHarbor AI Career Accelerator",
            o.BaseAmountInr, "INR", 30, false, false, false);
        var price = MembershipPricing.Calculate(plan, pricing.GetGstRate());
        return new(plan.Code, plan.Name, o.PlanVersion, price.BaseAmount, price.TaxRate, price.TaxAmount,
            price.TotalAmount, "SubscriptionBillingCycleUtc", new Dictionary<AcceleratorFeature, int>(o.Allowances),
            o.Enabled && plan.IsActive, o.Enabled && o.AIEnabled && configured is not null);
    }

    public async Task<IReadOnlyList<CanonicalAcceleratorSkill>> SkillsAsync(Guid actor, string? query, int page, int pageSize, CancellationToken ct)
    {
        await Candidate(actor, ct);
        Page(page, pageSize);
        if (query is { Length: > 100 }) throw Invalid("Skill search is too long.");
        return (await store.SearchSkillsAsync((query ?? string.Empty).Trim().ToUpperInvariant(), (page - 1) * pageSize, pageSize, ct))
            .Select(x => new CanonicalAcceleratorSkill(x.Id, x.Name)).ToArray();
    }

    public Task<CanonicalAcceleratorSkill> CreateCanonicalSkillAsync(Guid actor, CreateCanonicalAcceleratorSkillRequest request, CancellationToken ct) =>
        store.WriteAsync(async () =>
        {
            await Administrator(actor, ct);
            if (!Text(request.Name, 150)) throw Invalid("Invalid canonical skill name.");
            var name = string.Join(' ', request.Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var normalized = name.ToUpperInvariant();
            var existing = await store.SkillByNameAsync(normalized, ct);
            if (existing is not null) return new CanonicalAcceleratorSkill(existing.Id, existing.Name);
            var skill = new Skill { Name = name, NormalizedName = normalized };
            store.Add(skill);
            await Audit(actor, AuditAction.Create, "AcceleratorCanonicalSkill", skill.Id, ct);
            return new CanonicalAcceleratorSkill(skill.Id, skill.Name);
        }, ct);

    public async Task<TaxonomyResponse?> TaxonomyAsync(Guid actor, Guid? versionId, CancellationToken ct)
    {
        await Candidate(actor, ct);
        var taxonomy = await store.TaxonomyAsync(versionId, null, ct);
        return taxonomy is null ? null : TaxonomyDto(taxonomy);
    }

    public async Task<TaxonomyResponse?> AdminTaxonomyAsync(Guid actor, Guid? versionId, CancellationToken ct)
    {
        await Administrator(actor, ct);
        var taxonomy = await store.TaxonomyAsync(versionId, null, ct);
        return taxonomy is null ? null : TaxonomyDto(taxonomy);
    }

    public Task<TaxonomyResponse> PublishTaxonomyAsync(Guid actor, PublishTaxonomyRequest request, CancellationToken ct) =>
        store.WriteAsync(async () =>
        {
            await Administrator(actor, ct);
            ValidateTaxonomy(request);
            var hash = Hash(request);
            var existing = await store.TaxonomyAsync(null, request.Version, ct);
            if (existing is not null)
            {
                if (existing.ContentHash != hash) throw new ConflictException("Taxonomy version is immutable.", "accelerator_version_conflict");
                return TaxonomyDto(existing);
            }
            var canonical = await store.SkillsAsync(request.Skills.Select(x => x.SkillId).ToArray(), ct);
            if (canonical.Count != request.Skills.Length) throw Invalid("Every competency must reference an existing canonical skill.");
            var taxonomy = new AcceleratorTaxonomy { Version = request.Version, ContentHash = hash, PublishedAtUtc = Now };
            var skills = request.Skills.ToDictionary(x => x.SkillId, x => new AcceleratorCompetency
            {
                Taxonomy = taxonomy, TaxonomyId = taxonomy.Id, SkillId = x.SkillId,
                Skill = canonical.Single(s => s.Id == x.SkillId), SkillNameSnapshot = canonical.Single(s => s.Id == x.SkillId).Name,
                Description = x.Description.Trim(), Category = x.Category.Trim(),
                Difficulty = x.Difficulty, AssessmentMethodsJson = JsonSerializer.Serialize(x.AssessmentMethods),
                ResourcesJson = JsonSerializer.Serialize(x.LearningResources)
            });
            foreach (var definition in request.Skills)
            {
                var skill = skills[definition.SkillId];
                if (definition.ParentSkillId is { } parent)
                { skill.Parent = skills[parent]; skill.ParentId = skills[parent].Id; }
                foreach (var prerequisite in definition.PrerequisiteSkillIds)
                    skill.Prerequisites.Add(new AcceleratorPrerequisite { TaxonomyId = taxonomy.Id, Competency = skill,
                        CompetencyId = skill.Id, RequiredCompetency = skills[prerequisite], RequiredCompetencyId = skills[prerequisite].Id });
                taxonomy.Competencies.Add(skill);
            }
            foreach (var definition in request.Roles)
            {
                var role = new AcceleratorCareerRole { Taxonomy = taxonomy, TaxonomyId = taxonomy.Id,
                    Code = definition.Code, Name = definition.Name.Trim(), Description = definition.Description.Trim() };
                foreach (var requirement in definition.Requirements)
                    role.Requirements.Add(new AcceleratorRoleRequirement { Role = role, RoleId = role.Id, TaxonomyId = taxonomy.Id,
                        Competency = skills[requirement.SkillId], CompetencyId = skills[requirement.SkillId].Id,
                        ExpectedProficiency = requirement.ExpectedProficiency });
                taxonomy.Roles.Add(role);
            }
            store.Add(taxonomy);
            await Audit(actor, AuditAction.Publish, "AcceleratorTaxonomy", taxonomy.Id, ct);
            return TaxonomyDto(taxonomy);
        }, ct);

    public async Task<AcceleratorProfileResponse?> ProfileAsync(Guid actor, CancellationToken ct)
    {
        await Candidate(actor, ct);
        var profile = await store.ProfileAsync(actor, ct);
        return profile is null ? null : ProfileDto(profile);
    }

    public Task<AcceleratorProfileResponse> SaveProfileAsync(Guid actor, SaveAcceleratorProfileRequest request, CancellationToken ct) =>
        store.WriteAsync(async () =>
        {
            var user = await Candidate(actor, ct);
            if (request.ExperienceYears is < 0 or > 60 || decimal.Round(request.ExperienceYears, 1) != request.ExperienceYears ||
                !Text(request.EducationSummary, 2000, true) || !StudyTime(request.WeeklyStudyMinutes) ||
                request.Skills is not { Length: <= 100 } || request.Skills.Any(x => x is null || x.SkillId == Guid.Empty || x.Proficiency is < 0 or > 5) ||
                request.Skills.Select(x => x.SkillId).Distinct().Count() != request.Skills.Length)
                throw Invalid("Invalid profile or self-reported skills.");
            if (request.LinkCurrentResume && (user.ResumeUploadedAtUtc is null || string.IsNullOrWhiteSpace(user.ResumeFileName)))
                throw Invalid("Upload a resume using the existing candidate profile first.");
            var canonical = await store.SkillsAsync(request.Skills.Select(x => x.SkillId).ToArray(), ct);
            if (canonical.Count != request.Skills.Length) throw Invalid("Use canonical skill identifiers.");
            var profile = await store.ProfileAsync(actor, ct);
            if (profile is null)
            {
                if (request.ExpectedRevision.HasValue) throw new ConflictException("Profile changed; reload it.");
                profile = new() { UserId = actor };
                store.Add(profile);
            }
            else if (profile.Revision != request.ExpectedRevision) throw new ConflictException("Profile changed; reload it.");
            foreach (var skill in profile.Skills.Where(x => !x.IsDeleted))
                if (!request.Skills.Any(x => x.SkillId == skill.SkillId)) skill.IsDeleted = true;
            foreach (var skill in request.Skills)
            {
                var current = profile.Skills.FirstOrDefault(x => !x.IsDeleted && x.SkillId == skill.SkillId);
                if (current is not null) current.SelfReportedProficiency = skill.Proficiency;
                else profile.Skills.Add(new AcceleratorProfileSkill { Profile = profile, ProfileId = profile.Id,
                    SkillId = skill.SkillId, SelfReportedProficiency = skill.Proficiency });
            }
            profile.ExperienceYears = request.ExperienceYears;
            profile.EducationSummary = request.EducationSummary.Trim();
            profile.WeeklyStudyMinutes = request.WeeklyStudyMinutes;
            profile.LinkedResumeUploadedAtUtc = request.LinkCurrentResume ? user.ResumeUploadedAtUtc : null;
            profile.Revision = Guid.NewGuid();
            await Audit(actor, AuditAction.Update, "AcceleratorProfile", profile.Id, ct);
            return ProfileDto(profile);
        }, ct);

    public Task<CareerGoalResponse> CreateGoalAsync(Guid actor, CreateCareerGoalRequest request, CancellationToken ct) =>
        store.WriteAsync(async () =>
        {
            await Candidate(actor, ct);
            if (request.RequestKey == Guid.Empty || request.RoleId == Guid.Empty || !StudyTime(request.WeeklyStudyMinutes) ||
                request.TargetAtUtc.Kind != DateTimeKind.Utc) throw Invalid("Invalid career goal.");
            var hash = Hash(request);
            var previous = await store.GoalByKeyAsync(actor, request.RequestKey, ct);
            if (previous is not null)
            {
                if (previous.RequestHash != hash) throw new ConflictException("Idempotency key was used for a different goal.");
                return GoalDto(previous);
            }
            if (request.TargetAtUtc <= Now || request.TargetAtUtc > Now.AddYears(5)) throw Invalid("Target must be within the next five years.");
            if ((await store.GoalsAsync(actor, 0, 101, ct)).Count >= 100) throw Invalid("Archive/export existing goals before adding more than 100 goals.");
            var role = await store.RoleAsync(request.RoleId, ct) ?? throw new NotFoundException("Career role not found.");
            var goal = new AcceleratorCareerGoal { UserId = actor, Role = role, RoleId = role.Id, TaxonomyId = role.TaxonomyId,
                RequestKey = request.RequestKey, RequestHash = hash, WeeklyStudyMinutes = request.WeeklyStudyMinutes, TargetAtUtc = request.TargetAtUtc };
            store.Add(goal);
            await Audit(actor, AuditAction.Create, "AcceleratorCareerGoal", goal.Id, ct);
            return GoalDto(goal);
        }, ct);

    public async Task<CareerGoalResponse> GoalAsync(Guid actor, Guid id, CancellationToken ct)
    {
        await Candidate(actor, ct);
        return GoalDto(await store.GoalAsync(actor, id, ct) ?? throw new NotFoundException("Career goal not found."));
    }

    public Task<bool> EraseProfileAsync(Guid actor, CancellationToken ct) => store.WriteAsync(async () =>
    {
        await Candidate(actor, ct);
        var profile = await store.ProfileAsync(actor, ct);
        if (profile is null) return false;
        profile.IsDeleted = true; profile.EducationSummary = string.Empty; profile.LinkedResumeUploadedAtUtc = null;
        profile.ExperienceYears = 0; profile.WeeklyStudyMinutes = 15; profile.Revision = Guid.NewGuid();
        foreach (var skill in profile.Skills) skill.IsDeleted = true;
        await Audit(actor, AuditAction.Delete, "AcceleratorProfile", profile.Id, ct);
        return true;
    }, ct);

    public async Task<IReadOnlyList<CareerGoalResponse>> GoalsAsync(Guid actor, int page, int pageSize, CancellationToken ct)
    {
        await Candidate(actor, ct);
        Page(page, pageSize);
        return (await store.GoalsAsync(actor, (page - 1) * pageSize, pageSize, ct)).Select(GoalDto).ToArray();
    }

    public Task<CareerGoalResponse> ChangeGoalAsync(Guid actor, Guid id, ChangeCareerGoalRequest request, CancellationToken ct) =>
        store.WriteAsync(async () =>
        {
            await Candidate(actor, ct);
            if (!Enum.IsDefined(request.Status)) throw Invalid("Unknown goal state.");
            var goal = await store.GoalAsync(actor, id, ct) ?? throw new NotFoundException("Career goal not found.");
            if (goal.Revision != request.ExpectedRevision) throw new ConflictException("Goal changed; reload it.");
            if (goal.Status == AcceleratorGoalStatus.Archived && request.Status != goal.Status) throw new ConflictException("Archived goal history cannot be reopened.");
            goal.Status = request.Status;
            goal.Revision = Guid.NewGuid();
            await Audit(actor, AuditAction.Update, "AcceleratorCareerGoal", goal.Id, ct);
            return GoalDto(goal);
        }, ct);

    // Trusted internal seam only. Phase 6 must invoke it after the existing payment verifier commits.
    public Task<Guid> ActivateVerifiedPaymentAsync(Guid userId, Guid paymentId, CancellationToken ct) =>
        store.WriteAsync(() => ActivatePaymentCoreAsync(userId,paymentId,ct),ct);

    private async Task<Guid> ActivatePaymentCoreAsync(Guid userId,Guid paymentId,CancellationToken ct)
        {
            await Candidate(userId, ct);
            var payment = await store.PaymentAsync(paymentId, ct) ?? throw new NotFoundException("Verified payment not found.");
            if (payment.IsDeleted || payment.UserId != userId || payment.Status != PaymentStatus.Paid || payment.Provider == PaymentProvider.Unknown ||
                payment.PlanCode != AcceleratorOptions.PlanCode || payment.PaidAtUtc is null ||
                payment.PaidAtUtc.Value.Kind != DateTimeKind.Utc || payment.PaidAtUtc > Now ||
                string.IsNullOrWhiteSpace(payment.ProviderOrderId) || string.IsNullOrWhiteSpace(payment.ProviderPaymentId) ||
                payment.CurrencyCode != "INR" || payment.BaseAmount is not > 0 || payment.TaxRate is null ||
                payment.DurationDays is < 1 or > 366)
                throw new ConflictException("Payment does not establish Accelerator entitlement.", "accelerator_payment_unverified");
            var amount = MembershipPricing.Calculate(new MembershipPlan("Accelerator", payment.BaseAmount.Value,
                "INR", payment.DurationDays ?? 30), payment.TaxRate.Value);
            if (payment.Amount != amount.TotalAmount || payment.TaxAmount != amount.TaxAmount) throw new ConflictException("Payment pricing evidence does not match.");
            var existing = await store.CycleForPaymentAsync(paymentId, ct);
            if (existing is not null) return existing.Id;
            var latest = await store.LatestCycleAsync(userId, ct);
            var start = latest is not null && latest.EndsAtUtc > payment.PaidAtUtc.Value
                ? latest.EndsAtUtc : payment.PaidAtUtc.Value;
            var end = payment.DurationDays.HasValue ? start.AddDays(payment.DurationDays.Value) : start.AddMonths(1);
            if (await store.CycleOverlapsAsync(userId, start, end, ct)) throw new ConflictException("Accelerator billing cycles cannot overlap.");
            var cycle = new AcceleratorBillingCycle { UserId = userId, PaymentId = paymentId,
                PlanVersion = payment.PlanVersion ?? Settings.PlanVersion,
                StartsAtUtc = start, EndsAtUtc = end };
            foreach (var (feature, allocation) in Settings.Allowances)
                cycle.Allowances.Add(new AcceleratorAllowance { Cycle = cycle, CycleId = cycle.Id, Feature = feature, Allocated = allocation });
            store.Add(cycle);
            await Audit(userId, AuditAction.Confirm, "AcceleratorBillingCycle", cycle.Id, ct);
            return cycle.Id;
        }

    // Membership owns access. Billing cycles are the payment-backed quota projection, not a second subscription.
    // Call only within the existing Accelerator transaction/lock; no nested WriteAsync scopes.
    internal async Task<AcceleratorBillingCycle?> ResolveAccessCycleAsync(Guid actor,CancellationToken ct)
    {
        var membership=await store.AcceleratorMembershipAsync(actor,ct);
        if (membership is not null)
        {
            if (membership.IsDeleted || membership.Status!=MembershipStatus.Active || membership.StartsAtUtc>Now || membership.EndsAtUtc<=Now) return null;
            var payments=await store.AcceleratorMembershipPaymentsAsync(actor,membership.Id,ct);
            foreach(var payment in payments)
                if (await store.CycleForPaymentAsync(payment.Id,ct) is null)
                {
                    await ActivatePaymentCoreAsync(actor,payment.Id,ct);
                    // Flush inside the caller's transaction: subsequent renewals and quota queries
                    // must see the preceding projection. A failure rolls back the complete transaction.
                    await store.SaveAsync(ct);
                }
        }
        // Retain verified pre-Membership cycles for historical compatibility.
        return await store.ActiveCycleAsync(actor,Now,ct);
    }

    public Task<AcceleratorEntitlementResponse> EntitlementAsync(Guid actor, CancellationToken ct) => store.WriteAsync(async () =>
    {
        await Candidate(actor, ct);
        var membership=await store.AcceleratorMembershipAsync(actor,ct);
        var activeCycle=await ResolveAccessCycleAsync(actor,ct);
        var cycle = activeCycle ?? await store.LatestCycleAsync(actor, ct);
        if (membership?.IsDeleted==true) return new AcceleratorEntitlementResponse("Revoked",membership.StartsAtUtc,membership.EndsAtUtc,[]);
        // Creating an unpaid checkout also creates a Pending Membership row. It is not
        // a subscription; preserve the existing pre-verification API contract.
        if (membership?.Status==MembershipStatus.Pending && cycle is null)
            return new AcceleratorEntitlementResponse("NotSubscribed",null,null,[]);
        if (membership is not null && (membership.Status!=MembershipStatus.Active || membership.StartsAtUtc>Now || membership.EndsAtUtc<=Now))
            return new AcceleratorEntitlementResponse(membership.Status==MembershipStatus.Active ? (membership.StartsAtUtc>Now ? "Pending" : "Expired") : membership.Status.ToString(),membership.StartsAtUtc,membership.EndsAtUtc,[]);
        if (membership is not null && activeCycle is null)
            return new AcceleratorEntitlementResponse("ProvisioningRequired",membership.StartsAtUtc,membership.EndsAtUtc,[]);
        if (cycle is null) return new("NotSubscribed", null, null, []);
        var rows = await store.CycleUsageAsync(cycle.Id, ct);
        var status = cycle.Payment.Status != PaymentStatus.Paid || cycle.Payment.IsDeleted ? "Revoked" :
            Now >= cycle.EndsAtUtc ? "Expired" : Now < cycle.StartsAtUtc ? "Pending" : "Active";
        return new(status, cycle.StartsAtUtc, cycle.EndsAtUtc, cycle.Allowances.OrderBy(x => x.Feature).Select(a =>
        {
            var used = rows.Count(x => x.Feature == a.Feature && x.State is AcceleratorUsageState.Completed or AcceleratorUsageState.Uncertain);
            var held = rows.Count(x => x.Feature == a.Feature && x.State is AcceleratorUsageState.Reserved or AcceleratorUsageState.Dispatched);
            return new AcceleratorUsageSummary(a.Feature, a.Allocated, used, held, Math.Max(0, a.Allocated - used - held));
        }).ToArray());
    },ct);

    internal async Task<User> Candidate(Guid actor, CancellationToken ct)
    {
        Enabled();
        var user = await store.UserAsync(actor, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active || user.Role.Name != "Candidate") throw new UnauthorizedException();
        return user;
    }
    internal async Task Administrator(Guid actor, CancellationToken ct)
    {
        Enabled();
        var user = await store.UserAsync(actor, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active || user.Role.Name != "Administrator")
            throw new AppException("Administrator access required.", 403, "forbidden");
    }
    internal void Enabled()
    {
        if (!Settings.IsValid()) throw new InvalidOperationException("Invalid Accelerator configuration.");
        if (!Settings.Enabled) throw new AppException("Career Accelerator is unavailable.", 503, "accelerator_unavailable");
    }
    private Task Audit(Guid actor, AuditAction action, string type, Guid id, CancellationToken ct) =>
        audit.AppendAsync(new(action, type, id.ToString(), Actor: new(actor, type == "AcceleratorTaxonomy" ? "Administrator" : "Candidate")), ct);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static bool StudyTime(int minutes) => minutes is >= 15 and <= 10080;
    private static bool Text(string? text, int max, bool empty = false) => text is not null && text.Length <= max &&
        (empty || !string.IsNullOrWhiteSpace(text)) && !text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
    private static BadRequestException Invalid(string text) => new(text, "accelerator_invalid_request");
    internal static void Page(int page, int size)
    { if (page is < 1 or > 10000 || size is < 1 or > 100) throw Invalid("Invalid pagination."); }
    private static AcceleratorProfileResponse ProfileDto(AcceleratorProfile p) => new(p.Id, p.Revision, p.ExperienceYears,
        p.EducationSummary, p.WeeklyStudyMinutes, p.LinkedResumeUploadedAtUtc,
        p.Skills.Where(x => !x.IsDeleted).Select(x => new SelfReportedSkill(x.SkillId, x.SelfReportedProficiency)).ToArray());
    private static CareerGoalResponse GoalDto(AcceleratorCareerGoal g) => new(g.Id, g.Revision, g.RoleId, g.Role.Name,
        g.Role.Taxonomy.Version, g.Status, g.WeeklyStudyMinutes, g.TargetAtUtc);
    private static TaxonomyResponse TaxonomyDto(AcceleratorTaxonomy t) => new(t.Id, t.Version, t.PublishedAtUtc,
        t.Competencies.OrderBy(x => x.SkillNameSnapshot).Select(x => new CompetencyResponse(x.Id, x.SkillId, x.SkillNameSnapshot,
            x.Description, x.Category, x.Difficulty, x.Parent?.SkillId, x.Prerequisites.Select(p => p.RequiredCompetency.SkillId).ToArray(),
            JsonSerializer.Deserialize<string[]>(x.AssessmentMethodsJson)!, JsonSerializer.Deserialize<string[]>(x.ResourcesJson)!)).ToArray(),
        t.Roles.OrderBy(x => x.Code).Select(x => new CareerRoleResponse(x.Id, x.Code, x.Name, x.Description,
            x.Requirements.Select(r => new RoleRequirementDefinition(r.Competency.SkillId, r.ExpectedProficiency)).ToArray(),x.ConfigurationSource)).ToArray());

    private static void ValidateTaxonomy(PublishTaxonomyRequest request)
    {
        if (!AcceleratorOptions.Identifier(request.Version) || request.Skills is not { Length: >= 1 and <= 200 } ||
            request.Roles is not { Length: >= 1 and <= 100 } || request.Skills.Any(x => x is null) || request.Roles.Any(x => x is null) ||
            request.Skills.Select(x => x.SkillId).Distinct().Count() != request.Skills.Length ||
            request.Roles.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != request.Roles.Length)
            throw Invalid("Invalid taxonomy version or duplicate identifiers.");
        var ids = request.Skills.Select(x => x.SkillId).ToHashSet();
        foreach (var s in request.Skills)
        {
            if (s.SkillId == Guid.Empty || !Text(s.Description, 2000) || !Text(s.Category, 100) || s.Difficulty is < 1 or > 5 ||
                s.PrerequisiteSkillIds is not { Length: <= 30 } || s.PrerequisiteSkillIds.Distinct().Count() != s.PrerequisiteSkillIds.Length ||
                s.PrerequisiteSkillIds.Any(id => !ids.Contains(id)) || s.ParentSkillId is { } parent && !ids.Contains(parent) ||
                s.AssessmentMethods is not { Length: >= 1 and <= 6 } || s.AssessmentMethods.Any(m => !AssessmentMethods.Contains(m)) ||
                s.LearningResources is not { Length: <= 20 } || s.LearningResources.Any(u => u is null || u.Length > 2048 ||
                    !Uri.TryCreate(u, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length != 0))
                throw Invalid("Invalid competency definition.");
        }
        var edges = request.Skills.ToDictionary(x => x.SkillId, x => x.PrerequisiteSkillIds
            .Concat(x.ParentSkillId.HasValue ? [x.ParentSkillId.Value] : Array.Empty<Guid>()).ToArray());
        var complete = new HashSet<Guid>();
        foreach (var id in ids) Visit(id, []);
        foreach (var role in request.Roles)
            if (!AcceleratorOptions.Identifier(role.Code) || !Text(role.Name, 120) || !Text(role.Description, 2000) ||
                role.Requirements is not { Length: >= 1 and <= 100 } || role.Requirements.Any(x => x is null || !ids.Contains(x.SkillId) || x.ExpectedProficiency is < 1 or > 5) ||
                role.Requirements.Select(x => x.SkillId).Distinct().Count() != role.Requirements.Length)
                throw Invalid("Invalid role requirements.");
        void Visit(Guid id, HashSet<Guid> path)
        {
            if (complete.Contains(id)) return;
            if (!path.Add(id)) throw Invalid("Competency parent/prerequisite graph must be acyclic.");
            foreach (var next in edges[id]) Visit(next, path);
            path.Remove(id); complete.Add(id);
        }
    }
}
