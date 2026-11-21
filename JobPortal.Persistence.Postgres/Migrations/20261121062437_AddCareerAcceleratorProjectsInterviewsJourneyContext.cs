using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerAcceleratorProjectsInterviewsJourneyContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorReadinessSnapshots",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JourneyContextId",
                table: "AcceleratorReadinessSnapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorInterviewSessions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JourneyContextId",
                table: "AcceleratorInterviewSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorCandidateProjects",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JourneyContextId",
                table: "AcceleratorCandidateProjects",
                type: "uuid",
                nullable: true);

            // Resolve legacy ownership before adding required-context checks and foreign keys.
            migrationBuilder.Sql(Phase45JourneyBackfillSql.Up);

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorReadinessSnapshots_JourneyContextId",
                table: "AcceleratorReadinessSnapshots",
                column: "JourneyContextId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorReadinessSnapshots_UserId_JourneyContextId_Calcu~",
                table: "AcceleratorReadinessSnapshots",
                columns: new[] { "UserId", "JourneyContextId", "CalculatedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReadinessSnapshot_Journey",
                table: "AcceleratorReadinessSnapshots",
                sql: "\"JourneyContextId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewSessions_JourneyContextId",
                table: "AcceleratorInterviewSessions",
                column: "JourneyContextId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorInterviewSessions_UserId_JourneyContextId",
                table: "AcceleratorInterviewSessions",
                columns: new[] { "UserId", "JourneyContextId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InterviewSession_Journey",
                table: "AcceleratorInterviewSessions",
                sql: "\"JourneyContextId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCandidateProjects_JourneyContextId",
                table: "AcceleratorCandidateProjects",
                column: "JourneyContextId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorCandidateProjects_UserId_JourneyContextId",
                table: "AcceleratorCandidateProjects",
                columns: new[] { "UserId", "JourneyContextId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CandidateProject_Journey",
                table: "AcceleratorCandidateProjects",
                sql: "\"JourneyContextId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorCandidateProjects_AcceleratorJourneyContexts_Jou~",
                table: "AcceleratorCandidateProjects",
                column: "JourneyContextId",
                principalTable: "AcceleratorJourneyContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorInterviewSessions_AcceleratorJourneyContexts_Jou~",
                table: "AcceleratorInterviewSessions",
                column: "JourneyContextId",
                principalTable: "AcceleratorJourneyContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AcceleratorReadinessSnapshots_AcceleratorJourneyContexts_Jo~",
                table: "AcceleratorReadinessSnapshots",
                column: "JourneyContextId",
                principalTable: "AcceleratorJourneyContexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Phase45JourneyBackfillSql.Down);

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorCandidateProjects_AcceleratorJourneyContexts_Jou~",
                table: "AcceleratorCandidateProjects");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorInterviewSessions_AcceleratorJourneyContexts_Jou~",
                table: "AcceleratorInterviewSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_AcceleratorReadinessSnapshots_AcceleratorJourneyContexts_Jo~",
                table: "AcceleratorReadinessSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorReadinessSnapshots_JourneyContextId",
                table: "AcceleratorReadinessSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorReadinessSnapshots_UserId_JourneyContextId_Calcu~",
                table: "AcceleratorReadinessSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReadinessSnapshot_Journey",
                table: "AcceleratorReadinessSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorInterviewSessions_JourneyContextId",
                table: "AcceleratorInterviewSessions");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorInterviewSessions_UserId_JourneyContextId",
                table: "AcceleratorInterviewSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InterviewSession_Journey",
                table: "AcceleratorInterviewSessions");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorCandidateProjects_JourneyContextId",
                table: "AcceleratorCandidateProjects");

            migrationBuilder.DropIndex(
                name: "IX_AcceleratorCandidateProjects_UserId_JourneyContextId",
                table: "AcceleratorCandidateProjects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CandidateProject_Journey",
                table: "AcceleratorCandidateProjects");

            migrationBuilder.DropColumn(
                name: "JourneyContextId",
                table: "AcceleratorReadinessSnapshots");

            migrationBuilder.DropColumn(
                name: "JourneyContextId",
                table: "AcceleratorInterviewSessions");

            migrationBuilder.DropColumn(
                name: "JourneyContextId",
                table: "AcceleratorCandidateProjects");

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorReadinessSnapshots",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorInterviewSessions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GoalId",
                table: "AcceleratorCandidateProjects",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
