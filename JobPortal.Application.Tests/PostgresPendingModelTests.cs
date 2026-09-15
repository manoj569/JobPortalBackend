using JobPortal.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PostgresPendingModelTests
{
    [Fact]
    public void CandidateContractChangesRequireNoPostgresMigration()
    {
        using var context = new PostgresJobPortalDbContextFactory().CreateDbContext([]);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
