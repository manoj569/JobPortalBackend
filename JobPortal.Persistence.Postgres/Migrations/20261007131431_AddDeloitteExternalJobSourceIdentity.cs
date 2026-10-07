using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddDeloitteExternalJobSourceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalJobId",
                table: "Jobs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JobSourceId",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourcePostedAtUtc",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_JobSourceId",
                table: "Jobs",
                column: "JobSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_JobSourceId_ExternalJobId",
                table: "Jobs",
                columns: new[] { "JobSourceId", "ExternalJobId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"JobSourceId\" IS NOT NULL AND \"ExternalJobId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_JobSources_JobSourceId",
                table: "Jobs",
                column: "JobSourceId",
                principalTable: "JobSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_JobSources_JobSourceId",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_JobSourceId",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_JobSourceId_ExternalJobId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ExternalJobId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "JobSourceId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SourcePostedAtUtc",
                table: "Jobs");
        }
    }
}
