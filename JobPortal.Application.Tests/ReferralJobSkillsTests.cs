using JobPortal.Application.Features.Jobs;
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
    [Fact]
    public async Task ComposePersistsMoreThanTwentySkillsAndReusesCaseInsensitiveSkill()
    {
        await using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var company = new Company { Name = "Test company", Slug = "test-company" };
        var category = new Category { Name = "Test category", Slug = "test-category" };
        var existing = new Skill { Name = "C#", NormalizedName = "C#" };
        db.AddRange(company, category, existing);
        await db.SaveChangesAsync();
        var service = new JobService(new JobRepository(db), new UnitOfWork(db), new AuditWriterTestDouble(),
            new CreateJobRequestValidator(), new UpdateJobRequestValidator(), new UpdateRecruiterContactRequestValidator(),
            new JobSearchQueryValidator(), TimeProvider.System, new CompanyManagementRepository(db), new CategoryManagementRepository(db));
        var request = new ComposeJobRequest(new("Test job", MinimumExperienceYears: 2, MaximumExperienceYears: 5,
            Skills: Enumerable.Range(1, 25).Select(x => $" Skill {x} ").Concat([" C# ", "c#", "", " "]).ToArray()),
            new(company.Id), new(category.Id));

        Assert.True((await new ComposeJobRequestValidator().ValidateAsync(request)).IsValid);
        var result = await service.ComposeAsync(Guid.NewGuid(), request);
        db.ChangeTracker.Clear();
        var job = await db.Jobs.Include(x => x.JobSkills).ThenInclude(x => x.Skill).SingleAsync(x => x.Id == result.Id);
        Assert.Equal(26, job.JobSkills.Count);
        Assert.Equal(26, await db.Set<Skill>().CountAsync());
        Assert.Single(
            job.JobSkills,
            x => x.SkillId == existing.Id); Assert.All(job.JobSkills, x => Assert.Equal(x.Skill.Name.Trim(), x.Skill.Name));
        Assert.Equal(2, job.MinimumExperienceYears);
        Assert.Equal(5, job.MaximumExperienceYears);

        job.Status = JobStatus.Published;
        var referrer = new User { Status = UserStatus.Active };
        db.Add(new JobReferral { Job = job, ReferrerUser = referrer, ApprovalStatus = JobReferralApprovalStatus.Approved });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var page = await new JobReferralRepository(db).GetApprovedAsync(1, 20);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(26, Assert.Single(page.Items).Job.JobSkills.Count);

        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.BadRequestException>(() =>
            service.ComposeAsync(Guid.NewGuid(), request with { Job = request.Job with { MinimumExperienceYears = -1 } }));
    }

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
