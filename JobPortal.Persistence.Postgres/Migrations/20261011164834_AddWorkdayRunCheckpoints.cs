using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkdayRunCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_JobSourceRuns_ActiveSource",
                table: "JobSourceRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobSourceRuns_Status",
                table: "JobSourceRuns");

            migrationBuilder.AddColumn<string>(
                name: "CheckpointJson",
                table: "JobSourceRuns",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionCount",
                table: "JobSourceRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "JobSourceRunItems",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    IngestionOutcome = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<int>(type: "integer", nullable: false),
                    ValidationReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PublicationOutcome = table.Column<int>(type: "integer", nullable: true),
                    PublicationReasonsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CommittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublicationAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSourceRunItems", x => new { x.RunId, x.Ordinal });
                    table.CheckConstraint("CK_JobSourceRunItems_Ordinal", "\"Ordinal\" BETWEEN 0 AND 9999");
                    table.ForeignKey(
                        name: "FK_JobSourceRunItems_JobSourceRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "JobSourceRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_JobSourceRuns_ActiveSource",
                table: "JobSourceRuns",
                column: "JobSourceId",
                unique: true,
                filter: "\"Status\" IN (0, 1) OR \"Status\" = 5 OR (\"Status\" = 4 AND \"AttemptCount\" < 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobSourceRuns_Executions",
                table: "JobSourceRuns",
                sql: "\"ExecutionCount\" BETWEEN 0 AND 1000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobSourceRuns_Status",
                table: "JobSourceRuns",
                sql: "\"Status\" BETWEEN 0 AND 7");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobSourceRunItems");

            migrationBuilder.DropIndex(
                name: "UX_JobSourceRuns_ActiveSource",
                table: "JobSourceRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobSourceRuns_Executions",
                table: "JobSourceRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobSourceRuns_Status",
                table: "JobSourceRuns");

            migrationBuilder.DropColumn(
                name: "CheckpointJson",
                table: "JobSourceRuns");

            migrationBuilder.DropColumn(
                name: "ExecutionCount",
                table: "JobSourceRuns");

            migrationBuilder.CreateIndex(
                name: "UX_JobSourceRuns_ActiveSource",
                table: "JobSourceRuns",
                column: "JobSourceId",
                unique: true,
                filter: "\"Status\" IN (0, 1) OR (\"Status\" = 4 AND \"AttemptCount\" < 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobSourceRuns_Status",
                table: "JobSourceRuns",
                sql: "\"Status\" BETWEEN 0 AND 4");
        }
    }
}
