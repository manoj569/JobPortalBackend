using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAIApplyFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIApplyApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalApplicationUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    NormalizedApplicationUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    FailureKind = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequiresUserInput = table.Column<bool>(type: "boolean", nullable: false),
                    MatchScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplyApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIApplyApplications_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIApplyApplications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIApplyPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobTitlesJson = table.Column<string>(type: "text", nullable: false),
                    SkillsJson = table.Column<string>(type: "text", nullable: false),
                    PreferredLocationsJson = table.Column<string>(type: "text", nullable: false),
                    WorkplaceTypesJson = table.Column<string>(type: "text", nullable: false),
                    EmploymentTypesJson = table.Column<string>(type: "text", nullable: false),
                    MinimumExperience = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    MaximumExperience = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    MinimumSalary = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    MaximumSalary = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplyPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIApplyPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIApplyProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    WillingToRelocate = table.Column<bool>(type: "boolean", nullable: true),
                    WorkAuthorization = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VisaSponsorshipPreference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplyProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIApplyProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIApplyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleType = table.Column<int>(type: "integer", nullable: false),
                    Operator = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplyRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIApplyRules_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIApplySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    PreferredStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreferredDaysJson = table.Column<string>(type: "text", nullable: false),
                    AutoResumeApplications = table.Column<bool>(type: "boolean", nullable: false),
                    AllowAIGeneratedAnswers = table.Column<bool>(type: "boolean", nullable: false),
                    RequireConfirmationBeforeSubmit = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIApplySettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserApplicationAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NormalizedQuestion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Answer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserApplicationAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserApplicationAnswers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIApplyCosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AIRequestCount = table.Column<int>(type: "integer", nullable: false),
                    AIInputTokens = table.Column<long>(type: "bigint", nullable: false),
                    AIOutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    BrowserExecutionSeconds = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    ProxyCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplyCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIApplyCosts_AIApplyApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "AIApplyApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIApplyCosts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIApplyExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MessageCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplyExecutionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIApplyExecutionLogs_AIApplyApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "AIApplyApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIApplyQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NormalizedQuestion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    QuestionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SuggestedAnswer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FinalAnswer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AnsweredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIApplyQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIApplyQuestions_AIApplyApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "AIApplyApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIApplyQuestions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyApplications_IsDeleted",
                table: "AIApplyApplications",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyApplications_JobId",
                table: "AIApplyApplications",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyApplications_Status_ScheduledAtUtc_Priority",
                table: "AIApplyApplications",
                columns: new[] { "Status", "ScheduledAtUtc", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyApplications_UserId_JobId",
                table: "AIApplyApplications",
                columns: new[] { "UserId", "JobId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyApplications_UserId_NormalizedApplicationUrl",
                table: "AIApplyApplications",
                columns: new[] { "UserId", "NormalizedApplicationUrl" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyApplications_UserId_Status_CreatedAtUtc",
                table: "AIApplyApplications",
                columns: new[] { "UserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyCosts_ApplicationId",
                table: "AIApplyCosts",
                column: "ApplicationId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyCosts_IsDeleted",
                table: "AIApplyCosts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyCosts_UserId_CreatedAtUtc",
                table: "AIApplyCosts",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyExecutionLogs_ApplicationId_CreatedAtUtc",
                table: "AIApplyExecutionLogs",
                columns: new[] { "ApplicationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyExecutionLogs_IsDeleted",
                table: "AIApplyExecutionLogs",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyPreferences_IsDeleted",
                table: "AIApplyPreferences",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyPreferences_UserId",
                table: "AIApplyPreferences",
                column: "UserId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyProfiles_IsDeleted",
                table: "AIApplyProfiles",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyProfiles_UserId",
                table: "AIApplyProfiles",
                column: "UserId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyQuestions_ApplicationId",
                table: "AIApplyQuestions",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyQuestions_IsDeleted",
                table: "AIApplyQuestions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyQuestions_UserId_Status_CreatedAtUtc",
                table: "AIApplyQuestions",
                columns: new[] { "UserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyRules_IsDeleted",
                table: "AIApplyRules",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyRules_UserId_IsEnabled",
                table: "AIApplyRules",
                columns: new[] { "UserId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplySettings_Enabled_Paused",
                table: "AIApplySettings",
                columns: new[] { "Enabled", "Paused" });

            migrationBuilder.CreateIndex(
                name: "IX_AIApplySettings_IsDeleted",
                table: "AIApplySettings",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AIApplySettings_UserId",
                table: "AIApplySettings",
                column: "UserId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_UserApplicationAnswers_IsDeleted",
                table: "UserApplicationAnswers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_UserApplicationAnswers_UserId_IsActive",
                table: "UserApplicationAnswers",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserApplicationAnswers_UserId_NormalizedQuestion",
                table: "UserApplicationAnswers",
                columns: new[] { "UserId", "NormalizedQuestion" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIApplyCosts");

            migrationBuilder.DropTable(
                name: "AIApplyExecutionLogs");

            migrationBuilder.DropTable(
                name: "AIApplyPreferences");

            migrationBuilder.DropTable(
                name: "AIApplyProfiles");

            migrationBuilder.DropTable(
                name: "AIApplyQuestions");

            migrationBuilder.DropTable(
                name: "AIApplyRules");

            migrationBuilder.DropTable(
                name: "AIApplySettings");

            migrationBuilder.DropTable(
                name: "UserApplicationAnswers");

            migrationBuilder.DropTable(
                name: "AIApplyApplications");
        }
    }
}
