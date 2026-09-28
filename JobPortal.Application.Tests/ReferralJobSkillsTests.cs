using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.Referrals;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class ReferralJobSkillsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComposePersistsMoreThanTwentySkillsAndReusesCaseInsensitiveSkill(bool submitReferral)
    {
        await using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var company = new Company { Name = "Test company", Slug = "test-company" };
        var category = new Category { Name = "Test category", Slug = "test-category" };
        var existing = new Skill { Name = "C#", NormalizedName = "C#" };
        var referrer = new User { Status = UserStatus.Active };
        db.AddRange(company, category, existing, referrer);
        await db.SaveChangesAsync();
        var service = new JobService(new JobRepository(db), new UnitOfWork(db), new AuditWriterTestDouble(),
            new CreateJobRequestValidator(), new UpdateJobRequestValidator(), new UpdateRecruiterContactRequestValidator(),
            new JobSearchQueryValidator(), TimeProvider.System, new CompanyManagementRepository(db), new CategoryManagementRepository(db));
        var request = new ComposeJobRequest(new("Test job", MinimumExperienceYears: 2, MaximumExperienceYears: 5,
            Skills: Enumerable.Range(1, 25).Select(x => $" Skill {x} ").Concat([" C# ", "c#", "", " "]).ToArray()),
            new(company.Id), new(category.Id));

        Assert.True((await new ComposeJobRequestValidator().ValidateAsync(request)).IsValid);
        Guid jobId;
        if (submitReferral)
        {
            var referrals = new JobReferralService(new JobReferralRepository(db), new JobRepository(db), service,
                new MembershipRepository(db, TimeProvider.System), new AuditWriterTestDouble(), new UnitOfWork(db), TimeProvider.System,
                NotificationTestSupport.Outbox(db, TimeProvider.System));
            var result = await referrals.SubmitAsync(referrer.Id, new(request, null, true, false, false));
            Assert.Equal(JobReferralApprovalStatus.Pending, result.ApprovalStatus);
            jobId = result.JobId;
        }
        else jobId = (await service.ComposeAsync(referrer.Id, request)).Id;
        db.ChangeTracker.Clear();
        var job = await db.Jobs.Include(x => x.JobSkills).ThenInclude(x => x.Skill).SingleAsync(x => x.Id == jobId);
        Assert.Equal(26, job.JobSkills.Count);
        Assert.Equal(26, await db.Set<Skill>().CountAsync());
        Assert.Single(
            job.JobSkills,
            x => x.SkillId == existing.Id);
        Assert.All(job.JobSkills, x =>
        {
            Assert.Equal(x.Skill.Name.Trim(), x.Skill.Name);
            Assert.False(string.IsNullOrWhiteSpace(x.Skill.Name));
            Assert.Equal(JobSkill.DefaultProficiencyLevel, x.ProficiencyLevel);
            Assert.InRange(x.ProficiencyLevel, JobSkill.MinimumProficiencyLevel, JobSkill.MaximumProficiencyLevel);
        });
        Assert.Equal(2, job.MinimumExperienceYears);
        Assert.Equal(5, job.MaximumExperienceYears);

        await service.UpdateAsync(job.Id, new("Updated job", "Updated description", company.Id, category.Id,
            "https://example.test/apply", null, null, null, null, null, null, "USD",
            Enum.GetValues<EmploymentType>()[0], Enum.GetValues<WorkplaceType>()[0],
            Enum.GetValues<ExperienceLevel>()[0], null, MinimumExperienceYears: 2, MaximumExperienceYears: 5));
        Assert.Equal(26, await db.JobSkills.CountAsync(x => x.JobId == job.Id));
        Assert.All(await db.JobSkills.AsNoTracking().Where(x => x.JobId == job.Id).ToArrayAsync(),
            x => Assert.Equal(JobSkill.DefaultProficiencyLevel, x.ProficiencyLevel));

        job.Status = JobStatus.Published;
        if (submitReferral)
            (await db.JobReferrals.SingleAsync(x => x.JobId == job.Id)).ApprovalStatus = JobReferralApprovalStatus.Approved;
        else
            db.Add(new JobReferral { Job = job, ReferrerUserId = referrer.Id, ApprovalStatus = JobReferralApprovalStatus.Approved });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var page = await new JobReferralRepository(db).GetApprovedAsync(1, 20);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(26, Assert.Single(page.Items).Job.JobSkills.Count);

        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.BadRequestException>(() =>
            service.ComposeAsync(Guid.NewGuid(), request with { Job = request.Job with { MinimumExperienceYears = -1 } }));
    }

    [Fact]
    public void NewSkillDefaultsToRequiredWithoutSpecifiedProficiency()
    {
        Assert.Equal((byte)1, JobSkill.DefaultProficiencyLevel);
        Assert.Equal(JobSkill.DefaultProficiencyLevel, new JobSkill().ProficiencyLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(255)]
    public void InvalidProficiencyIsRejectedBeforePersistence(byte value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JobSkill { ProficiencyLevel = value });
        var skill = new JobSkill { ProficiencyLevel = 3 };
        Assert.Throws<ArgumentOutOfRangeException>(() => skill.ProficiencyLevel = value);
        Assert.Equal((byte)3, skill.ProficiencyLevel);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ValidExplicitProficiencyIsPreserved(byte value) =>
        Assert.Equal(value, new JobSkill { ProficiencyLevel = value }.ProficiencyLevel);

    [Theory]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public void SkillLengthIsValidatedAfterTrimming(int length, bool valid)
    {
        var request = new ComposeJobRequest(new("Test job", Skills: [" " + new string('a', length) + " ", " "]),
            Category: new(Guid.NewGuid()));
        Assert.Equal(valid, new ComposeJobRequestValidator().Validate(request).IsValid);
    }
}
