using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateProfileCompletionPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CandidateSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Proficiency = table.Column<int>(type: "integer", nullable: true),
                    YearsOfExperience = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateSkills", x => x.Id);
                    table.CheckConstraint("CK_CandidateSkills_Proficiency", "\"Proficiency\" IS NULL OR \"Proficiency\" BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_CandidateSkills_YearsOfExperience", "\"YearsOfExperience\" IS NULL OR (\"YearsOfExperience\" >= 0 AND \"YearsOfExperience\" <= 50)");
                    table.ForeignKey(
                        name: "FK_CandidateSkills_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSkills_IsDeleted",
                table: "CandidateSkills",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSkills_UserId",
                table: "CandidateSkills",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSkills_UserId_NormalizedName",
                table: "CandidateSkills",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateSkills");
        }
    }
}
