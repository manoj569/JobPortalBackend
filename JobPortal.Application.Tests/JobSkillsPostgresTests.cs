using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class JobSkillsPostgresTests
{
    // Same opt-in localhost-only pattern as the existing PostgreSQL constraint tests.
    [LocalJobSkillsPostgresFact]
    public async Task ComposedSkillsSatisfyTheProductionPostgresConstraint()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("JOB_SKILLS_TEST_POSTGRES"));
        Assert.True(settings.Host is "localhost" or "127.0.0.1" or "::1");
        Assert.Equal("job_skills_test", settings.Database);
        settings.IncludeErrorDetail = false;
        settings.Timeout = 5;
        settings.CommandTimeout = 15;
        settings.Pooling = false;

        await using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var company = new Company { Name = "Test", Slug = "test" };
        var category = new Category { Name = "Test", Slug = "test" };
        db.AddRange(company, category);
        await db.SaveChangesAsync();
        var service = new JobService(new JobRepository(db), new UnitOfWork(db), new AuditWriterTestDouble(),
            new CreateJobRequestValidator(), new UpdateJobRequestValidator(), new UpdateRecruiterContactRequestValidator(),
            new JobSearchQueryValidator(), TimeProvider.System, new CompanyManagementRepository(db), new CategoryManagementRepository(db));
        await service.ComposeAsync(Guid.NewGuid(), new(new("Senior engineer", MinimumExperienceYears: 10,
            Skills: Enumerable.Range(1, 26).Select(i => $"Skill {i}").ToArray()), new(company.Id), new(category.Id)));
        var skills = await db.JobSkills.AsNoTracking().ToArrayAsync();
        Assert.Equal(26, skills.Length);
        var constraint = new InitialPostgresSchema().UpOperations.OfType<CreateTableOperation>()
            .Single(t => t.Name == "JobSkills").CheckConstraints.Single(c => c.Name == "CK_JobSkills_ProficiencyLevel");

        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        // Temporary table is session-local and disappears on disposal; no application schema is touched.
        await using (var create = new NpgsqlCommand($"CREATE TEMP TABLE \"JobSkills\" (\"ProficiencyLevel\" smallint NOT NULL, CONSTRAINT \"CK_JobSkills_ProficiencyLevel\" CHECK ({constraint.Sql}))", connection))
            await create.ExecuteNonQueryAsync();
        foreach (var skill in skills)
        {
            Assert.Equal(JobSkill.DefaultProficiencyLevel, skill.ProficiencyLevel);
            await using var insert = new NpgsqlCommand("INSERT INTO pg_temp.\"JobSkills\" (\"ProficiencyLevel\") VALUES (@level)", connection);
            insert.Parameters.AddWithValue("level", (short)skill.ProficiencyLevel);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        await using var invalid = new NpgsqlCommand("INSERT INTO pg_temp.\"JobSkills\" (\"ProficiencyLevel\") VALUES (0)", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync());
        Assert.Equal("23514", error.SqlState);
        Assert.Equal("CK_JobSkills_ProficiencyLevel", error.ConstraintName);
    }

    private sealed class LocalJobSkillsPostgresFactAttribute : FactAttribute
    {
        public LocalJobSkillsPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JOB_SKILLS_TEST_POSTGRES")))
                Skip = "Requires explicit JOB_SKILLS_TEST_POSTGRES for a disposable localhost job_skills_test database; never uses application configuration.";
        }
    }
}
