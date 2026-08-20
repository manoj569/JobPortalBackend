using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateProfileExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AvailabilityToJoin",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CandidateEmploymentTypesJson",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "CandidateJobTypesJson",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentAnnualSalary",
                table: "Users",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentArea",
                table: "Users",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentCity",
                table: "Users",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentCountry",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentFixedAnnualSalary",
                table: "Users",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentVariableAnnualSalary",
                table: "Users",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedAnnualSalary",
                table: "Users",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOutsideIndia",
                table: "Users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredCitiesJson",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "PreferredJobRolesJson",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "PreferredShiftsJson",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "WorkStatus",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualSalary",
                table: "CandidateExperiences",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoticePeriod",
                table: "CandidateExperiences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkillsUsedJson",
                table: "CandidateExperiences",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "CourseType",
                table: "CandidateEducation",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GradingSystem",
                table: "CandidateEducation",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrentlyStudying",
                table: "CandidateEducation",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CandidateProfilePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SizeBytes = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateProfilePhotos", x => x.Id);
                    table.CheckConstraint("CK_CandidateProfilePhotos_SizeBytes", "\"SizeBytes\" BETWEEN 1 AND 1048576");
                    table.ForeignKey(
                        name: "FK_CandidateProfilePhotos_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateProfilePhotos_IsDeleted",
                table: "CandidateProfilePhotos",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateProfilePhotos_UserId",
                table: "CandidateProfilePhotos",
                column: "UserId",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateProfilePhotos");

            migrationBuilder.DropColumn(
                name: "AvailabilityToJoin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CandidateEmploymentTypesJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CandidateJobTypesJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CurrentAnnualSalary",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CurrentArea",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CurrentCity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CurrentCountry",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CurrentFixedAnnualSalary",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CurrentVariableAnnualSalary",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExpectedAnnualSalary",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsOutsideIndia",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreferredCitiesJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreferredJobRolesJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreferredShiftsJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WorkStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AnnualSalary",
                table: "CandidateExperiences");

            migrationBuilder.DropColumn(
                name: "NoticePeriod",
                table: "CandidateExperiences");

            migrationBuilder.DropColumn(
                name: "SkillsUsedJson",
                table: "CandidateExperiences");

            migrationBuilder.DropColumn(
                name: "CourseType",
                table: "CandidateEducation");

            migrationBuilder.DropColumn(
                name: "GradingSystem",
                table: "CandidateEducation");

            migrationBuilder.DropColumn(
                name: "IsCurrentlyStudying",
                table: "CandidateEducation");
        }
    }
}
