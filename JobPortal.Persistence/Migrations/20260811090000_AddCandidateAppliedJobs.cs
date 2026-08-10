using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace JobPortal.Persistence.Migrations;

[DbContext(typeof(JobPortalDbContext))]
[Migration("20260811090000_AddCandidateAppliedJobs")]
public partial class AddCandidateAppliedJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "ApplicationMethod", table: "JobApplications",
            type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.DropIndex(name: "IX_JobApplications_UserId_JobId", table: "JobApplications");
        migrationBuilder.CreateIndex(name: "IX_JobApplications_UserId_JobId", table: "JobApplications",
            columns: new[] { "UserId", "JobId" }, unique: true, filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_JobApplications_UserId_JobId", table: "JobApplications");
        migrationBuilder.DropColumn(name: "ApplicationMethod", table: "JobApplications");
        migrationBuilder.CreateIndex(name: "IX_JobApplications_UserId_JobId", table: "JobApplications",
            columns: new[] { "UserId", "JobId" }, unique: true);
    }
}
