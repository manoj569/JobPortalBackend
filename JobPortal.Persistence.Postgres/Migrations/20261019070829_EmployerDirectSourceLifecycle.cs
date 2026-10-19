using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EmployerDirectSourceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastCompleteSnapshotCount",
                table: "JobSources",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastReconciledSnapshotId",
                table: "JobSources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissingCompleteSnapshotThreshold",
                table: "JobSources",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceClosedBySnapshotId",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobSourceVacancies",
                columns: table => new
                {
                    JobSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalJobId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MissingCompleteSnapshots = table.Column<int>(type: "integer", nullable: false),
                    LastSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSourceVacancies", x => new { x.JobSourceId, x.ExternalJobId });
                    table.ForeignKey(
                        name: "FK_JobSourceVacancies_JobSources_JobSourceId",
                        column: x => x.JobSourceId,
                        principalTable: "JobSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobSourceVacancies_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobSourceVacancies_JobId",
                table: "JobSourceVacancies",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobSourceVacancies");

            migrationBuilder.DropColumn(
                name: "LastCompleteSnapshotCount",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "LastReconciledSnapshotId",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "MissingCompleteSnapshotThreshold",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "SourceClosedBySnapshotId",
                table: "Jobs");
        }
    }
}
