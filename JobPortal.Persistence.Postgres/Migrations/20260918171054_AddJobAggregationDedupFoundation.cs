using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAggregationDedupFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FingerprintHash",
                table: "Jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSeenAtUtc",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAtUtc",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CareerPageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AtsType = table.Column<int>(type: "integer", nullable: false),
                    AtsIdentifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ScanIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    LastRunAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulRunAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobSources_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_FingerprintHash",
                table: "Jobs",
                column: "FingerprintHash",
                filter: "\"IsDeleted\" = FALSE AND \"FingerprintHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_FirstSeenAtUtc",
                table: "Jobs",
                column: "FirstSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_LastSeenAtUtc",
                table: "Jobs",
                column: "LastSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_CompanyId",
                table: "JobSources",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_CompanyId_AtsType_AtsIdentifier",
                table: "JobSources",
                columns: new[] { "CompanyId", "AtsType", "AtsIdentifier" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_IsActive",
                table: "JobSources",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_IsActive_LastRunAtUtc",
                table: "JobSources",
                columns: new[] { "IsActive", "LastRunAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JobSources_IsDeleted",
                table: "JobSources",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobSources");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_FingerprintHash",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_FirstSeenAtUtc",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_LastSeenAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "FingerprintHash",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "FirstSeenAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUtc",
                table: "Jobs");
        }
    }
}
