using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateCompanySubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Companies",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SubmissionSource",
                table: "Companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedByCandidateId",
                table: "Companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Companies"
                SET "NormalizedName" = lower(regexp_replace(trim("Name"), '\s+', ' ', 'g'));
                ALTER TABLE "Companies" ALTER COLUMN "NormalizedName" DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_NormalizedName",
                table: "Companies",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_SubmittedByCandidateId_CreatedAtUtc",
                table: "Companies",
                columns: new[] { "SubmittedByCandidateId", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Users_SubmittedByCandidateId",
                table: "Companies",
                column: "SubmittedByCandidateId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Users_SubmittedByCandidateId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_NormalizedName",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_SubmittedByCandidateId_CreatedAtUtc",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SubmissionSource",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SubmittedByCandidateId",
                table: "Companies");
        }
    }
}
