using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class GenericEmployerSourceIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmployerDiagnosticsJson",
                table: "JobSources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployerStrategyJson",
                table: "JobSources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastInventoryAtUtc",
                table: "JobSources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastInventoryDigest",
                table: "JobSources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastInventoryVersion",
                table: "JobSources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastObservedAtUtc",
                table: "JobSources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastObservedSnapshotId",
                table: "JobSources",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmployerDiagnosticsJson",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "EmployerStrategyJson",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "LastInventoryAtUtc",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "LastInventoryDigest",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "LastInventoryVersion",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "LastObservedAtUtc",
                table: "JobSources");

            migrationBuilder.DropColumn(
                name: "LastObservedSnapshotId",
                table: "JobSources");
        }
    }
}
