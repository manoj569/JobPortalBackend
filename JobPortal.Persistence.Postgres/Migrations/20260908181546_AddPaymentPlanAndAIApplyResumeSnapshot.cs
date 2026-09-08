using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentPlanAndAIApplyResumeSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanCode",
                table: "Payments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumeContentType",
                table: "AIApplyApplications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumeFileName",
                table: "AIApplyApplications",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ResumeSizeBytes",
                table: "AIApplyApplications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumeStorageKey",
                table: "AIApplyApplications",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanCode",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ResumeContentType",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "ResumeFileName",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "ResumeSizeBytes",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "ResumeStorageKey",
                table: "AIApplyApplications");
        }
    }
}
