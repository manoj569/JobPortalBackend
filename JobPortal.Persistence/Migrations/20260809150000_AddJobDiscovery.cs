using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace JobPortal.Persistence.Migrations;

public partial class AddJobDiscovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "JobDiscoveryRuns", columns: table => new
        {
            Id = table.Column<Guid>(nullable: false), Trigger = table.Column<string>(maxLength: 32, nullable: false),
            Status = table.Column<string>(maxLength: 32, nullable: false), StartedAtUtc = table.Column<DateTime>(nullable: false),
            CompletedAtUtc = table.Column<DateTime>(nullable: true), CandidateCount = table.Column<int>(nullable: false),
            DuplicateCount = table.Column<int>(nullable: false), ImportedCount = table.Column<int>(nullable: false),
            ErrorSummary = table.Column<string>(maxLength: 2000, nullable: true), CreatedAtUtc = table.Column<DateTime>(nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(nullable: true), IsDeleted = table.Column<bool>(nullable: false), DeletedAtUtc = table.Column<DateTime>(nullable: true)
        }, constraints: table => table.PrimaryKey("PK_JobDiscoveryRuns", x => x.Id));
        migrationBuilder.CreateTable(name: "JobDiscoveryItems", columns: table => new
        {
            Id = table.Column<Guid>(nullable: false), RunId = table.Column<Guid>(nullable: false), Provider = table.Column<string>(maxLength: 64, nullable: false),
            SourceJobId = table.Column<string>(maxLength: 256, nullable: false), Title = table.Column<string>(maxLength: 300, nullable: false),
            CompanyName = table.Column<string>(maxLength: 200, nullable: false), CategoryName = table.Column<string>(maxLength: 200, nullable: false),
            ApplicationUrl = table.Column<string>(maxLength: 2048, nullable: false), Location = table.Column<string>(maxLength: 300, nullable: true),
            Description = table.Column<string>(nullable: true), EmploymentType = table.Column<string>(maxLength: 50, nullable: true), PublishedAtUtc = table.Column<DateTime>(nullable: true),
            Status = table.Column<string>(maxLength: 32, nullable: false), DuplicateReason = table.Column<string>(maxLength: 64, nullable: true),
            ExistingJobId = table.Column<Guid>(nullable: true), ImportedJobId = table.Column<Guid>(nullable: true), CreatedAtUtc = table.Column<DateTime>(nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(nullable: true), IsDeleted = table.Column<bool>(nullable: false), DeletedAtUtc = table.Column<DateTime>(nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_JobDiscoveryItems", x => x.Id); table.ForeignKey("FK_JobDiscoveryItems_JobDiscoveryRuns_RunId", x => x.RunId, "JobDiscoveryRuns", "Id", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateIndex("IX_JobDiscoveryRuns_StartedAtUtc", "JobDiscoveryRuns", "StartedAtUtc");
        migrationBuilder.CreateIndex("IX_JobDiscoveryItems_RunId", "JobDiscoveryItems", "RunId");
        migrationBuilder.CreateIndex("IX_JobDiscoveryItems_Provider_SourceJobId", "JobDiscoveryItems", new[] { "Provider", "SourceJobId" }, unique: true, filter: "[IsDeleted] = 0");
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable("JobDiscoveryItems"); migrationBuilder.DropTable("JobDiscoveryRuns"); }
}
