using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobPortal.Persistence.Postgres;

public sealed class PostgresJobPortalDbContextFactory : IDesignTimeDbContextFactory<JobPortalDbContext>
{
    public JobPortalDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=jobportal_design;Username=postgres;SSL Mode=Disable";
        var options = new DbContextOptionsBuilder<JobPortalDbContext>();
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(PostgresMigrationMarker).Assembly.FullName));
        return new JobPortalDbContext(options.Options);
    }
}
