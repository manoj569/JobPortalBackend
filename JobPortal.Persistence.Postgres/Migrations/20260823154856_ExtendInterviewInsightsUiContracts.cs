using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ExtendInterviewInsightsUiContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InterviewFormat",
                table: "InterviewInsights",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApproximateTimeOfDay",
                table: "CandidateInterviewSchedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedRoundTypes",
                table: "CandidateInterviewSchedules",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InterviewFormat",
                table: "CandidateInterviewSchedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreparationStatus",
                table: "CandidateInterviewSchedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReminderRequested",
                table: "CandidateInterviewSchedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterviewFormat",
                table: "InterviewInsights");

            migrationBuilder.DropColumn(
                name: "ApproximateTimeOfDay",
                table: "CandidateInterviewSchedules");

            migrationBuilder.DropColumn(
                name: "ExpectedRoundTypes",
                table: "CandidateInterviewSchedules");

            migrationBuilder.DropColumn(
                name: "InterviewFormat",
                table: "CandidateInterviewSchedules");

            migrationBuilder.DropColumn(
                name: "PreparationStatus",
                table: "CandidateInterviewSchedules");

            migrationBuilder.DropColumn(
                name: "ReminderRequested",
                table: "CandidateInterviewSchedules");
        }
    }
}
