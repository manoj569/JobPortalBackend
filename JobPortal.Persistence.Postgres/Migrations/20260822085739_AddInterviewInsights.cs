using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CandidateInterviewSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoleTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    InterviewAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConfirmFeedbackAvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FeedbackNotificationSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateInterviewSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateInterviewSchedules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CandidateInterviewSchedules_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CandidateInterviewSchedules_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InterviewInsights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorCandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoleTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ExperienceLevel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    InterviewDateMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    OverallDifficulty = table.Column<int>(type: "integer", nullable: false),
                    ProcessSummary = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    PreparationTips = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: true),
                    IsAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModerationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HelpfulConfirmedCount = table.Column<int>(type: "integer", nullable: false),
                    QualityScore = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewInsights", x => x.Id);
                    table.CheckConstraint("CK_InterviewInsights_HelpfulCount", "\"HelpfulConfirmedCount\" >= 0");
                    table.CheckConstraint("CK_InterviewInsights_QualityScore", "\"QualityScore\" >= 0");
                    table.ForeignKey(
                        name: "FK_InterviewInsights_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InterviewInsights_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InterviewInsights_Users_AuthorCandidateId",
                        column: x => x.AuthorCandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InsightHelpfulnessFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InsightId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateInterviewScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Helpfulness = table.Column<int>(type: "integer", nullable: false),
                    InterviewMatch = table.Column<int>(type: "integer", nullable: false),
                    Feedback = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsightHelpfulnessFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsightHelpfulnessFeedback_CandidateInterviewSchedules_Cand~",
                        column: x => x.CandidateInterviewScheduleId,
                        principalTable: "CandidateInterviewSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InsightHelpfulnessFeedback_InterviewInsights_InsightId",
                        column: x => x.InsightId,
                        principalTable: "InterviewInsights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InsightHelpfulnessFeedback_Users_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InsightReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InsightId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterCandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsightReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsightReports_InterviewInsights_InsightId",
                        column: x => x.InsightId,
                        principalTable: "InterviewInsights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InsightReports_Users_ReporterCandidateId",
                        column: x => x.ReporterCandidateId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InterviewRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewInsightId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    RoundType = table.Column<int>(type: "integer", nullable: false),
                    RoundTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    QuestionsOrTopics = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    CandidateAdvice = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewRounds", x => x.Id);
                    table.CheckConstraint("CK_InterviewRounds_Duration", "\"DurationMinutes\" IS NULL OR (\"DurationMinutes\" BETWEEN 1 AND 1440)");
                    table.ForeignKey(
                        name: "FK_InterviewRounds_InterviewInsights_InterviewInsightId",
                        column: x => x.InterviewInsightId,
                        principalTable: "InterviewInsights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateInterviewSchedules_CandidateId_CompanyId_Interview~",
                table: "CandidateInterviewSchedules",
                columns: new[] { "CandidateId", "CompanyId", "InterviewAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateInterviewSchedules_CompanyId",
                table: "CandidateInterviewSchedules",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateInterviewSchedules_FeedbackNotificationSentAtUtc_C~",
                table: "CandidateInterviewSchedules",
                columns: new[] { "FeedbackNotificationSentAtUtc", "ConfirmFeedbackAvailableAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateInterviewSchedules_JobId",
                table: "CandidateInterviewSchedules",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_InsightHelpfulnessFeedback_CandidateId_CreatedAtUtc",
                table: "InsightHelpfulnessFeedback",
                columns: new[] { "CandidateId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InsightHelpfulnessFeedback_CandidateId_InsightId",
                table: "InsightHelpfulnessFeedback",
                columns: new[] { "CandidateId", "InsightId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_InsightHelpfulnessFeedback_CandidateInterviewScheduleId",
                table: "InsightHelpfulnessFeedback",
                column: "CandidateInterviewScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_InsightHelpfulnessFeedback_InsightId",
                table: "InsightHelpfulnessFeedback",
                column: "InsightId");

            migrationBuilder.CreateIndex(
                name: "IX_InsightReports_InsightId",
                table: "InsightReports",
                column: "InsightId");

            migrationBuilder.CreateIndex(
                name: "IX_InsightReports_ReporterCandidateId_InsightId",
                table: "InsightReports",
                columns: new[] { "ReporterCandidateId", "InsightId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_InsightReports_Status_CreatedAtUtc",
                table: "InsightReports",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InterviewInsights_AuthorCandidateId_CreatedAtUtc",
                table: "InterviewInsights",
                columns: new[] { "AuthorCandidateId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InterviewInsights_CompanyId_Status_PublishedAtUtc",
                table: "InterviewInsights",
                columns: new[] { "CompanyId", "Status", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InterviewInsights_JobId",
                table: "InterviewInsights",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewRounds_InterviewInsightId_Sequence",
                table: "InterviewRounds",
                columns: new[] { "InterviewInsightId", "Sequence" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsightHelpfulnessFeedback");

            migrationBuilder.DropTable(
                name: "InsightReports");

            migrationBuilder.DropTable(
                name: "InterviewRounds");

            migrationBuilder.DropTable(
                name: "CandidateInterviewSchedules");

            migrationBuilder.DropTable(
                name: "InterviewInsights");
        }
    }
}
