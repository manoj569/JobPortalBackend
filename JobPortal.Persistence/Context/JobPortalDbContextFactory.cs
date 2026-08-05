using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobPortal.Persistence.Context;

public class JobPortalDbContextFactory : IDesignTimeDbContextFactory<JobPortalDbContext>
{
    public JobPortalDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection environment variable is not set.");

        var optionsBuilder = new DbContextOptionsBuilder<JobPortalDbContext>();
        optionsBuilder.UseSqlServer(connStr);

        return new JobPortalDbContext(optionsBuilder.Options);
    }
}
