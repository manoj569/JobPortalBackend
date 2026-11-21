using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployerHiringPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorTutorConversations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JourneyContextId",
                table: "AcceleratorTutorConversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LessonId",
                table: "AcceleratorTutorConversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModuleId",
                table: "AcceleratorTutorConversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorPracticeSessions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JourneyContextId",
                table: "AcceleratorPracticeSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LessonId",
                table: "AcceleratorPracticeSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModuleId",
                table: "AcceleratorPracticeSessions",
                type: "uuid",
                nullable: true);

            // Phase 3 columns were originally scaffolded with this migration. Backfill must precede its NOT NULL checks.
            migrationBuilder.Sql(Phase3JourneyBackfillSql.Up);

            migrationBuilder.CreateTable(
                name: "EmployerHiringPipelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    ScreeningStatus = table.Column<int>(type: "integer", nullable: false),
                    ScreeningNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ScreeningScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    ScreeningStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScreenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScreenedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfferStatus = table.Column<int>(type: "integer", nullable: false),
                    OfferNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OfferAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployerHiringPipelines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployerHiringPipelines_JobApplications_JobApplicationId",
                        column: x => x.JobApplicationId,
                        principalTable: "JobApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployerHiringPipelines_Users_ScreenedByUserId",
                        column: x => x.ScreenedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_JourneyContextId",
                table: "AcceleratorTutorConversations",
                column: "JourneyContextId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_LessonId",
                table: "AcceleratorTutorConversations",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_ModuleId",
                table: "AcceleratorTutorConversations",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorTutorConversations_UserId_JourneyContextId_Creat~",
                table: "AcceleratorTutorConversations",
                columns: new[] { "UserId", "JourneyContextId", "CreatedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceleratorTutorConversation_Journey",
                table: "AcceleratorTutorConversations",
                sql: "\"JourneyContextId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeSessions_JourneyContextId",
                table: "AcceleratorPracticeSessions",
                column: "JourneyContextId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeSessions_LessonId",
                table: "AcceleratorPracticeSessions",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeSessions_ModuleId",
                table: "AcceleratorPracticeSessions",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorPracticeSessions_UserId_JourneyContextId_Created~",
                table: "AcceleratorPracticeSessions",
                columns: new[] { "UserId", "JourneyContextId", "CreatedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceleratorPracticeSession_Journey",
                table: "AcceleratorPracticeSessions",
                sql: "\"JourneyContextId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerHiringPipelines_IsDeleted",
                table: "EmployerHiringPipelines",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerHiringPipelines_JobApplicationId",
                table: "EmployerHiringPipelines",
                column: "JobApplicationId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerHiringPipelines_ScreenedByUserId",
                table: "EmployerHiringPipelines",
                column: "ScreenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployerHiringPipelines_Stage_ScreeningStatus",
                table: "EmployerHiringPipelines",
                columns: new[] { "Stage", "ScreeningStatus" });

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorPracticeSessions_AcceleratorJourneyContexts_Jour~",
                table: "AcceleratorPracticeSessions",
                column: "JourneyContextId",
                principalTable: "AcceleratorJourneyContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorPracticeSessions_AcceleratorLearningModules_Modu~",
                table: "AcceleratorPracticeSessions",
                column: "ModuleId",
                principalTable: "AcceleratorLearningModules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorPracticeSessions_AcceleratorLessons_LessonId",
                table: "AcceleratorPracticeSessions",
                column: "LessonId",
                principalTable: "AcceleratorLessons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorTutorConversations_AcceleratorJourneyContexts_Jo~",
                table: "AcceleratorTutorConversations",
                column: "JourneyContextId",
                principalTable: "AcceleratorJourneyContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorTutorConversations_AcceleratorLearningModules_Mo~",
                table: "AcceleratorTutorConversations",
                column: "ModuleId",
                principalTable: "AcceleratorLearningModules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorTutorConversations_AcceleratorLessons_LessonId",
                table: "AcceleratorTutorConversations",
                column: "LessonId",
                principalTable: "AcceleratorLessons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Phase3JourneyBackfillSql.AssertLegacyOnly);

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorPracticeSessions_AcceleratorJourneyContexts_Jour~",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorPracticeSessions_AcceleratorLearningModules_Modu~",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorPracticeSessions_AcceleratorLessons_LessonId",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorTutorConversations_AcceleratorJourneyContexts_Jo~",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorTutorConversations_AcceleratorLearningModules_Mo~",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorTutorConversations_AcceleratorLessons_LessonId",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropTable(
                name: "EmployerHiringPipelines");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorTutorConversations_JourneyContextId",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorTutorConversations_LessonId",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorTutorConversations_ModuleId",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorTutorConversations_UserId_JourneyContextId_Creat~",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceleratorTutorConversation_Journey",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorPracticeSessions_JourneyContextId",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorPracticeSessions_LessonId",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorPracticeSessions_ModuleId",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorPracticeSessions_UserId_JourneyContextId_Created~",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceleratorPracticeSession_Journey",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropColumn(
                name: "JourneyContextId",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropColumn(
                name: "LessonId",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropColumn(
                name: "ModuleId",
                table: "AcceleratorTutorConversations");

            migrationBuilder.DropColumn(
                name: "JourneyContextId",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropColumn(
                name: "LessonId",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.DropColumn(
                name: "ModuleId",
                table: "AcceleratorPracticeSessions");

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorTutorConversations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorPracticeSessions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
