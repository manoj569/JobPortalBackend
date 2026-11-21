using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorRecognitionPostgresTests
{
    private static readonly string[] AllowedDatabases = ["accelerator_2b_verify", "accelerator_phase3_verify", "accelerator_phase45_verify"];
    private static readonly Guid Owner=Guid.Parse("2a000000-0000-0000-0000-000000000001");
    private static readonly Guid Context=Guid.Parse("2a000000-0000-0000-0000-000000000006");
    private static JobPortalDbContext Open()
    {
        var connection=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ACCELERATOR_RECOGNITION_TEST_POSTGRES"));
        Assert.Equal("127.0.0.1",connection.Host); Assert.Equal(55439,connection.Port);
        Assert.Contains(connection.Database, AllowedDatabases);
        return new(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(connection.ConnectionString,pg=> { pg.MigrationsAssembly(typeof(PostgresMigrationMarker).Assembly.FullName); pg.EnableRetryOnFailure(3); }).Options);
    }
    private static AcceleratorRecognitionService Service(JobPortalDbContext db)
    {
        var writes=new AcceleratorStore(db); var journeys=new AcceleratorJourneyStore(db); var learning=new AcceleratorLearningPathStore(db);
        var access=new AcceleratorService(writes,Options.Create(new AcceleratorOptions { Enabled=true }),TimeProvider.System,new AuditWriterTestDouble(),new AcceleratorTestPricing());
        var contexts=new AcceleratorJourneyContextService(writes,journeys,learning,new AcceleratorPhase2Store(db),access);
        return new(writes,new AcceleratorRecognitionStore(db),contexts,journeys,learning,access,new AuditWriterTestDouble(),TimeProvider.System);
    }
    [RecognitionPostgresFact]
    public async Task PoliciesAwardsAndEligibilityPersistWithDatabaseConstraintsAndConcurrentReplay()
    {
        Guid policyId; Guid definitionId;
        await using (var setup=Open())
        {
            var admin=new User { Email=Guid.NewGuid()+"@example.invalid",NormalizedEmail=Guid.NewGuid()+"@EXAMPLE.INVALID",FirstName="Verification",LastName="Admin",RoleId=await setup.Roles.Where(x=>x.Name=="Administrator").Select(x=>x.Id).SingleAsync(),Status=UserStatus.Active };
            setup.Add(admin);
            var template=new AcceleratorAssessmentTemplate { RoleId=Guid.Parse("2a000000-0000-0000-0000-000000000004"),TaxonomyId=Guid.Parse("2a000000-0000-0000-0000-000000000003"),Version=Guid.NewGuid().ToString("N"),TimeLimitMinutes=30,MaximumQuestions=1,IsActive=true };
            var attempt=new AcceleratorAssessmentAttempt { UserId=Owner,GoalId=Context,JourneyContextId=Context,TemplateId=template.Id,TemplateVersion=template.Version,
                TaxonomyId=Guid.Parse("2a000000-0000-0000-0000-000000000003"),RequestKey=Guid.NewGuid(),RequestHash=new string('A',64),Status=AcceleratorAssessmentStatus.Completed,Score=80,AttemptNumber=1,ExpiresAtUtc=DateTime.UtcNow.AddHours(1) };
            setup.AddRange(template,attempt,new AcceleratorSkillEvidence { UserId=Owner,GoalId=Context,JourneyContextId=Context,CompetencyId=Guid.Parse("2a000000-0000-0000-0000-000000000005"),
                AttemptId=attempt.Id,Source=AcceleratorEvidenceSource.AssessmentDemonstrated,Confidence=1,Proficiency=AcceleratorProficiency.Intermediate,ScoringVersion="postgres-2b",EvaluatedAtUtc=DateTime.UtcNow });
            await setup.SaveChangesAsync();
            var service=Service(setup);
            policyId=await service.CreatePolicyAsync(admin.Id,AcceleratorRecognitionTests.Policy() with { Code="pg-"+Guid.NewGuid().ToString("N") },default);
            definitionId=await service.CreateAchievementAsync(admin.Id,new("pg-"+Guid.NewGuid().ToString("N"),"Configured award","PostgreSQL fixture",AcceleratorAchievementRule.MinimumProficiency,65),default);
            Assert.Equal(80m,(await service.ProficiencyAsync(Owner,Context,default)).Score);
        }
        await Task.WhenAll(Enumerable.Range(0,8).Select(async _=>
        {
            await using var db=Open(); var service=Service(db);
            await service.EvaluateAchievementsAsync(Owner,Context,default);
            var certificate=await service.EvaluateCertificateAsync(Owner,Context,default);
            Assert.Equal(AcceleratorCertificateState.Eligible,certificate.State); Assert.Null(certificate.IssuedAtUtc); Assert.Null(certificate.CertificateIdentifier);
        }));
        await using (var verify=Open())
        {
            Assert.Single(await verify.CandidateAcceleratorAchievements.Where(x=>x.JourneyContextId==Context && x.AchievementDefinitionId==definitionId).ToArrayAsync());
            Assert.Single(await verify.AcceleratorEnrollmentCertificates.Where(x=>x.JourneyContextId==Context).ToArrayAsync());
        }
        await using (var invalid=Open())
        {
            invalid.Add(new AcceleratorProficiencyBand { PolicyId=policyId,Label="Overlap",MinimumScore=60,MaximumScore=80,IsActive=true });
            var error=await Assert.ThrowsAsync<DbUpdateException>(()=>invalid.SaveChangesAsync());
            Assert.Equal("23P01",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using (var duplicate=Open())
        {
            duplicate.Add(new CandidateAcceleratorAchievement { UserId=Owner,JourneyContextId=Context,AchievementDefinitionId=definitionId,AwardedAtUtc=DateTime.UtcNow });
            var error=await Assert.ThrowsAsync<DbUpdateException>(()=>duplicate.SaveChangesAsync());
            Assert.Equal("23505",Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
    }
    private sealed class RecognitionPostgresFactAttribute : FactAttribute
    {
        public RecognitionPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCELERATOR_RECOGNITION_TEST_POSTGRES"))) Skip="Requires the disposable Docker Migration 2B database.";
        }
    }
}
