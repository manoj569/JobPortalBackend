using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace JobPortal.Persistence.Migrations;

[DbContext(typeof(JobPortalDbContext))]
[Migration("20260810090000_AddCandidateResumeRecommendations")]
public partial class AddCandidateResumeRecommendations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "CandidateResumeProfiles", columns: table => new
        {
            Id = table.Column<Guid>(nullable: false), UserId = table.Column<Guid>(nullable: false),
            ExtractionStatus = table.Column<int>(nullable: false), SkillsJson = table.Column<string>(type: "nvarchar(4000)", nullable: false),
            RoleKeywordsJson = table.Column<string>(type: "nvarchar(2000)", nullable: false),
            EducationKeywordsJson = table.Column<string>(type: "nvarchar(2000)", nullable: false),
            LocationsJson = table.Column<string>(type: "nvarchar(2000)", nullable: false),
            YearsOfExperience = table.Column<decimal>(type: "decimal(4,1)", nullable: true),
            ExtractionError = table.Column<string>(maxLength: 1000, nullable: true), ExtractedAtUtc = table.Column<DateTime>(nullable: true),
            CreatedAtUtc = table.Column<DateTime>(nullable: false), UpdatedAtUtc = table.Column<DateTime>(nullable: true),
            IsDeleted = table.Column<bool>(nullable: false), DeletedAtUtc = table.Column<DateTime>(nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CandidateResumeProfiles", x => x.Id);
            table.ForeignKey("FK_CandidateResumeProfiles_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_CandidateResumeProfiles_ExtractionStatus_ExtractedAtUtc", "CandidateResumeProfiles", new[] { "ExtractionStatus", "ExtractedAtUtc" });
        migrationBuilder.CreateIndex("IX_CandidateResumeProfiles_IsDeleted", "CandidateResumeProfiles", "IsDeleted");
        migrationBuilder.CreateIndex("IX_CandidateResumeProfiles_UserId", "CandidateResumeProfiles", "UserId", unique: true, filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("CandidateResumeProfiles");
}
