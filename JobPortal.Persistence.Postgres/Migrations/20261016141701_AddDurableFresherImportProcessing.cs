using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableFresherImportProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAtUtc",
                table: "AcquisitionImportRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttempts",
                table: "AcquisitionImportRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StagedRequestJson",
                table: "AcquisitionImportRuns",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionImportRuns_NextAttemptAtUtc_Id",
                table: "AcquisitionImportRuns",
                columns: new[] { "NextAttemptAtUtc", "Id" },
                filter: "\"StagedRequestJson\" IS NOT NULL AND \"Status\" NOT IN ('Completed', 'Failed') AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_AcquisitionImportItems_AcquisitionImportRunId_ItemIndex",
                table: "AcquisitionImportItems",
                columns: new[] { "AcquisitionImportRunId", "ItemIndex" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcquisitionImportRuns_NextAttemptAtUtc_Id",
                table: "AcquisitionImportRuns");

            migrationBuilder.DropIndex(
                name: "IX_AcquisitionImportItems_AcquisitionImportRunId_ItemIndex",
                table: "AcquisitionImportItems");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "AcquisitionImportRuns");

            migrationBuilder.DropColumn(
                name: "ProcessingAttempts",
                table: "AcquisitionImportRuns");

            migrationBuilder.DropColumn(
                name: "StagedRequestJson",
                table: "AcquisitionImportRuns");
        }
    }
}
