using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class NotificationOutboxTests
{
    [Fact]
    public void PendingSchemaDeltaIsLimitedToTheNotificationFoundation()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only", o => o.MigrationsAssembly("JobPortal.Persistence.Postgres")).Options);
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var prior = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        var delta = db.GetService<IMigrationsModelDiffer>().GetDifferences(prior.GetRelationalModel(), current.GetRelationalModel());
        Assert.NotEmpty(delta);
        Assert.All(delta, operation => Assert.True(operation is CreateTableOperation or AddColumnOperation or CreateIndexOperation or AddCheckConstraintOperation,
            $"Unexpected schema operation: {operation.GetType().Name}"));
        Assert.Equal("NotificationDeliveries", Assert.Single(delta.OfType<CreateTableOperation>()).Name);
        Assert.Equal(new[] { "CandidateInterviewSchedules.ReminderOffsetMinutes", "CandidateInterviewSchedules.ReminderRevision", "CandidateInterviewSchedules.TimeZoneId", "Notifications.BusinessKey" },
            delta.OfType<AddColumnOperation>().Select(x => x.Table + "." + x.Name).Order().ToArray());
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(delta, current).Select(x => x.CommandText));
        Assert.DoesNotContain("xmin", sql); Assert.DoesNotContain("JobId1", sql);
        Assert.DoesNotContain("DROP ", sql); Assert.DoesNotContain("Salary", sql);
    }
    [Fact]
    public void IntentHasDeterministicRecipientScopedChannelKeys()
    {
        var recipient = Guid.NewGuid();
        var source = Guid.NewGuid();
        var due = new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc);
        var first = NotificationOutbox.Create(NotificationSource.InterviewReminder, source, Guid.Empty,
            recipient, "interview:test", "Interview reminder", "Prepare for your interview.", "/dashboard/interview-insights", due);
        var repeat = NotificationOutbox.Create(NotificationSource.InterviewReminder, source, Guid.Empty,
            recipient, "interview:test", "Interview reminder", "Prepare for your interview.", "/dashboard/interview-insights", due);
        Assert.Equal(2, first.Count);
        Assert.Equal(first.Select(x => x.Id), repeat.Select(x => x.Id));
        Assert.Single(first.Select(x => x.NotificationId).Distinct());
        Assert.All(first, x => { Assert.Equal(recipient, x.UserId); Assert.Equal(due, x.NextAttemptAtUtc); });
        Assert.NotEqual(first[0].Id, first[1].Id);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/dashboard/referrals?redirect=https://evil.example")]
    public void RejectsUntrustedActionRoutes(string route) => Assert.False(NotificationOutbox.IsSafeActionUrl(route));

    [Fact]
    public void ModelEnforcesBusinessAndDeliveryUniqueness()
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseNpgsql("Host=localhost;Database=notification_model_only;Username=postgres").Options);
        var delivery = db.Model.FindEntityType(typeof(NotificationDelivery))!;
        Assert.Contains(delivery.GetIndexes(), x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "BusinessKey", "UserId", "Channel" }));
        var inbox = db.Model.FindEntityType(typeof(Notification))!;
        Assert.Contains(inbox.GetIndexes(), x => x.IsUnique && x.Properties.Any(p => p.Name == "BusinessKey"));
    }
}
