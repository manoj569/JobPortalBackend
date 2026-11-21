using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorRecognitionTests
{
    [Theory]
    [InlineData(AcceleratorLearningPathType.Career)]
    [InlineData(AcceleratorLearningPathType.Skill)]
    public async Task EnrollmentRecognitionUsesOwnedPathAndPersistedCompletion(AcceleratorLearningPathType type)
    {
        using var f=new AcceleratorFixture();
        var taxonomy=await f.Service.PublishTaxonomyAsync(f.Admin.Id,f.Taxonomy(),default);
        var competency=await f.Db.AcceleratorCompetencies.SingleAsync(x=>x.SkillId==f.CodeSkill.Id);
        var path=new AcceleratorLearningPath { Type=type,Slug="recognition-path",Title="Configured path",Difficulty=1,Status=AcceleratorLearningCatalogStatus.Published,
            CanonicalCareerRoleId=type==AcceleratorLearningPathType.Career ? taxonomy.Roles.Single(x=>x.Code=="backend").Id : null,
            CanonicalSkillId=type==AcceleratorLearningPathType.Skill ? f.CodeSkill.Id : null };
        var module=new AcceleratorLearningModule { LearningPath=path,LearningPathId=path.Id,Title="Configured module",Sequence=1,Difficulty=1,IsRequired=true,Status=AcceleratorLearningCatalogStatus.Published };
        var lesson=new AcceleratorLesson { Module=module,ModuleId=module.Id,Title="Configured lesson",Sequence=1,IsRequired=true,Status=AcceleratorLearningCatalogStatus.Published };
        var enrollment=new AcceleratorPathEnrollment { UserId=f.Candidate.Id,LearningPath=path,LearningPathId=path.Id,Status=AcceleratorLearningEnrollmentStatus.InProgress,TargetLevel=3,WeeklyCommitmentMinutes=300 };
        f.Db.AddRange(path,module,lesson,enrollment,new AcceleratorLessonProgress { Enrollment=enrollment,EnrollmentId=enrollment.Id,LessonId=lesson.Id,Status=AcceleratorLessonProgressStatus.Completed,CompletedAtUtc=f.Clock.Utc });
        if (type==AcceleratorLearningPathType.Skill) f.Db.Add(new AcceleratorLearningPathCompetencyRequirement { LearningPathId=path.Id,CompetencyId=competency.Id,TargetProficiency=3,Weight=1,IsRequired=true });
        await f.Db.SaveChangesAsync();
        var journeys=new AcceleratorJourneyStore(f.Db); var learning=new AcceleratorLearningPathStore(f.Db);
        var contexts=new AcceleratorJourneyContextService(f.Store,journeys,learning,f.Phase2Store,f.Service);
        var context=await contexts.GetOrCreateForEnrollmentAsync(f.Candidate.Id,enrollment.Id,default);
        var service=new AcceleratorRecognitionService(f.Store,new AcceleratorRecognitionStore(f.Db),contexts,journeys,learning,f.Service,f.Audit,f.Clock);
        await service.CreatePolicyAsync(f.Admin.Id,Policy() with { LearningPathId=path.Id,CertificateRequiresModules=true },default);
        await Evidence(f,context,80);
        var response=await service.EnrollmentAsync(f.Candidate.Id,enrollment.Id,default);
        Assert.Equal(path.Title,response.Title); Assert.Equal(80m,response.Proficiency.Score);
        Assert.Equal(1,response.Proficiency.CompletedModules); Assert.Equal(1,response.Proficiency.TotalModules);
        Assert.Equal(AcceleratorCertificateState.Eligible,(await service.EvaluateCertificateAsync(f.Candidate.Id,context.Id,default)).State);
        Assert.Empty(await f.Db.AcceleratorCareerGoals.ToArrayAsync());
        await Assert.ThrowsAsync<NotFoundException>(()=>service.EnrollmentAsync(f.Other.Id,enrollment.Id,default));
    }
    [Theory]
    [InlineData(0,"Developing")]
    [InlineData(70,"Advanced")]
    [InlineData(100,"Advanced")]
    public async Task ConfiguredAssessmentPolicyResolvesBoundaryScoresAndReplays(int score,string expectedBand)
    {
        using var f=new AcceleratorFixture(); var (service,context)=await Setup(f);
        await service.CreatePolicyAsync(f.Admin.Id,Policy(),default);
        await Evidence(f,context,score);
        var result=await service.ProficiencyAsync(f.Candidate.Id,context.Id,default);
        Assert.Equal((decimal)score,result.Score); Assert.Equal(expectedBand,result.Band);
        Assert.Equal(result.Score,(await service.ProficiencyAsync(f.Candidate.Id,context.Id,default)).Score);
        await Assert.ThrowsAsync<NotFoundException>(()=>service.ProficiencyAsync(f.Other.Id,context.Id,default));
    }

    [Fact]
    public void OverlappingAndInvalidWeightsAreRejected()
    {
        var request=Policy();
        Assert.Throws<BadRequestException>(()=>AcceleratorRecognitionService.ValidatePolicy(request with { Bands=[new("A",0,80,1),new("B",70,100,2)] }));
        Assert.Throws<BadRequestException>(()=>AcceleratorRecognitionService.ValidatePolicy(request with { Components=[new(AcceleratorProficiencyComponent.Assessment,99,true)] }));
    }

    [Fact]
    public async Task UnavailableRequiredPracticeDoesNotManufactureProficiency()
    {
        using var f=new AcceleratorFixture(); var (service,context)=await Setup(f);
        await service.CreatePolicyAsync(f.Admin.Id,Policy() with { Components=[new(AcceleratorProficiencyComponent.Assessment,50,true),new(AcceleratorProficiencyComponent.Practice,50,true)] },default);
        await Evidence(f,context,80);
        var result=await service.ProficiencyAsync(f.Candidate.Id,context.Id,default);
        Assert.Null(result.Score); Assert.Equal("Incomplete",result.Status);
        Assert.Contains(result.Components,x=>x.Component==AcceleratorProficiencyComponent.Practice && x.Score==null && x.Status=="Unavailable");
    }

    [Fact]
    public async Task AwardsAndCertificateAreServerDerivedIdempotentAndCandidateOwned()
    {
        using var f=new AcceleratorFixture(); var (service,context)=await Setup(f);
        await service.CreatePolicyAsync(f.Admin.Id,Policy(),default);
        await service.CreateAchievementAsync(f.Admin.Id,new("score-achievement","Demonstrated assessment","Test definition",AcceleratorAchievementRule.MinimumProficiency,65),default);
        Assert.Empty(await service.EvaluateAchievementsAsync(f.Candidate.Id,context.Id,default));
        Assert.Equal(AcceleratorCertificateState.NotEligible,(await service.EvaluateCertificateAsync(f.Candidate.Id,context.Id,default)).State);
        await Evidence(f,context,80);
        await Task.WhenAll(Enumerable.Range(0,4).Select(_=>service.EvaluateAchievementsAsync(f.Candidate.Id,context.Id,default)));
        Assert.Single(await service.AchievementsAsync(f.Candidate.Id,context.Id,default));
        var certificate=await service.EvaluateCertificateAsync(f.Candidate.Id,context.Id,default);
        Assert.Equal(AcceleratorCertificateState.Eligible,certificate.State); Assert.Null(certificate.IssuedAtUtc); Assert.Null(certificate.CertificateIdentifier);
        Assert.Equal(certificate.Id,(await service.EvaluateCertificateAsync(f.Candidate.Id,context.Id,default)).Id);
        await Task.WhenAll(Enumerable.Range(0,4).Select(_=>service.EvaluateCertificateAsync(f.Candidate.Id,context.Id,default)));
        Assert.Single(await f.Db.AcceleratorEnrollmentCertificates.ToArrayAsync());
        await Assert.ThrowsAsync<NotFoundException>(()=>service.AchievementsAsync(f.Other.Id,context.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.CertificateAsync(f.Other.Id,context.Id,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>service.EvaluateCertificateAsync(f.Other.Id,context.Id,default));
        await Assert.ThrowsAsync<AppException>(()=>service.CreatePolicyAsync(f.Candidate.Id,Policy() with { Code="forbidden" },default));
    }

    [Fact]
    public async Task RealPracticeContributesConfiguredWeightWithoutGrantingMastery()
    {
        using var f = new AcceleratorFixture();
        var setup = await AcceleratorPhase3JourneyTests.Setup(f, 2);
        var practice = AcceleratorPhase3JourneyTests.Create(f);
        var session = await practice.StartPracticeAsync(f.Candidate.Id, AcceleratorPhase3JourneyTests.Practice(setup), default);
        var journeys = new AcceleratorJourneyStore(f.Db); var learning = new AcceleratorLearningPathStore(f.Db);
        var contexts = new AcceleratorJourneyContextService(f.Store, journeys, learning, f.Phase2Store, f.Service);
        var context = await contexts.GetOwnedContextAsync(f.Candidate.Id, session.JourneyContextId!.Value, default);
        var service = new AcceleratorRecognitionService(f.Store, new AcceleratorRecognitionStore(f.Db), contexts, journeys, learning, f.Service, f.Audit, f.Clock);
        await service.CreatePolicyAsync(f.Admin.Id, Policy() with { Components = [new(AcceleratorProficiencyComponent.Assessment, 50, true), new(AcceleratorProficiencyComponent.Practice, 50, true)] }, default);
        await Evidence(f, context, 80);
        Assert.Null((await service.ProficiencyAsync(f.Candidate.Id, context.Id, default)).Score);
        await practice.SubmitPracticeAnswerAsync(f.Candidate.Id, session.Id, new(Guid.NewGuid(), session.Questions[0].Id, "{\"answer\":\"A\"}"), default);
        var result = await service.ProficiencyAsync(f.Candidate.Id, context.Id, default);
        Assert.Equal(90m, result.Score);
        Assert.Contains(result.Components, x => x.Component == AcceleratorProficiencyComponent.Practice && x.Score == 100 && x.Status == "Available");
        Assert.Single(result.EvidenceIds);
        Assert.DoesNotContain(await f.Db.AcceleratorSkillEvidence.ToArrayAsync(), x => x.ScoringVersion == "practice-objective-v1" && x.Confidence >= .5m);
    }

    internal static CreateProficiencyPolicyRequest Policy() => new(null,"verification-policy",1,[new(AcceleratorProficiencyComponent.Assessment,100,true)],
        [new("Developing",0,70,1),new("Advanced",70,100,2)],0.5m,1,60,false);

    private static async Task<(AcceleratorRecognitionService,AcceleratorJourneyContext)> Setup(AcceleratorFixture f)
    {
        var taxonomy=await f.Service.PublishTaxonomyAsync(f.Admin.Id,f.Taxonomy(),default);
        var goal=await f.Service.CreateGoalAsync(f.Candidate.Id,f.Goal(taxonomy.Roles[0].Id),default);
        var journeys=new AcceleratorJourneyStore(f.Db); var learning=new AcceleratorLearningPathStore(f.Db);
        var contexts=new AcceleratorJourneyContextService(f.Store,journeys,learning,f.Phase2Store,f.Service);
        var context=await contexts.GetOrCreateForCareerGoalAsync(f.Candidate.Id,goal.Id,default);
        return (new(f.Store,new AcceleratorRecognitionStore(f.Db),contexts,journeys,learning,f.Service,f.Audit,f.Clock),context);
    }
    private static async Task Evidence(AcceleratorFixture f,AcceleratorJourneyContext context,decimal score)
    {
        var competency=await f.Db.AcceleratorCompetencies.FirstAsync(x=>x.TaxonomyId==context.TaxonomyId);
        var template=new AcceleratorAssessmentTemplate { RoleId=context.CareerGoalId.HasValue ? (await f.Store.GoalAsync(f.Candidate.Id,context.CareerGoalId.Value,default))!.RoleId : null, TaxonomyId=context.TaxonomyId,
            Version=Guid.NewGuid().ToString("N"),TimeLimitMinutes=30,MaximumQuestions=1,IsActive=true };
        var attempt=new AcceleratorAssessmentAttempt { UserId=f.Candidate.Id,GoalId=context.CareerGoalId,JourneyContextId=context.Id,TemplateId=template.Id,TemplateVersion=template.Version,
            TaxonomyId=context.TaxonomyId,RequestKey=Guid.NewGuid(),RequestHash=new string('A',64),Status=AcceleratorAssessmentStatus.Completed,Score=score,AttemptNumber=1,ExpiresAtUtc=f.Clock.Utc.AddHours(1) };
        f.Db.AddRange(template,attempt,new AcceleratorSkillEvidence { UserId=f.Candidate.Id,GoalId=context.CareerGoalId,JourneyContextId=context.Id,CompetencyId=competency.Id,
            AttemptId=attempt.Id,Source=AcceleratorEvidenceSource.AssessmentDemonstrated,Confidence=1,Proficiency=AcceleratorProficiency.Intermediate,ScoringVersion="test",EvaluatedAtUtc=f.Clock.Utc });
        await f.Db.SaveChangesAsync();
    }
}
