using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAIApplyReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAtUtc",
                table: "AIApplyApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeadLetteredAtUtc",
                table: "AIApplyApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureClassification",
                table: "AIApplyApplications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstFailureAtUtc",
                table: "AIApplyApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailureAtUtc",
                table: "AIApplyApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "AIApplyApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "AIApplyApplications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmissionAttemptedAtUtc",
                table: "AIApplyApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmissionConfirmedAtUtc",
                table: "AIApplyApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIApplyApplications_Status_LeaseExpiresAtUtc",
                table: "AIApplyApplications",
                columns: new[] { "Status", "LeaseExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AIApplyApplications_Status_LeaseExpiresAtUtc",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "ClaimedAtUtc",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAtUtc",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "FailureClassification",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "FirstFailureAtUtc",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "LastFailureAtUtc",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "SubmissionAttemptedAtUtc",
                table: "AIApplyApplications");

            migrationBuilder.DropColumn(
                name: "SubmissionConfirmedAtUtc",
                table: "AIApplyApplications");
        }
    }
}
