using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerConsultantDraftOnboardingProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "YearsOfExperience",
                table: "CareerConsultants",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1);

            migrationBuilder.AlterColumn<DateTime>(
                name: "TermsAcceptedAtUtc",
                table: "CareerConsultants",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<int>(
                name: "ProfessionalType",
                table: "CareerConsultants",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "FunctionalArea",
                table: "CareerConsultants",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "CareerConsultants",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "CareerConsultants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfessionalEmail",
                table: "CareerConsultants",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageUrl",
                table: "CareerConsultants",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublicProfileConsentAtUtc",
                table: "CareerConsultants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "CareerConsultants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CareerConsultantEducation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Qualification = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Institution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FieldOfStudy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StartYear = table.Column<int>(type: "integer", nullable: true),
                    EndYear = table.Column<int>(type: "integer", nullable: true),
                    IsCurrentlyStudying = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerConsultantEducation", x => x.Id);
                    table.CheckConstraint("CK_CareerConsultantEducation_Order", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("CK_CareerConsultantEducation_Years", "(\"StartYear\" IS NULL OR \"StartYear\" BETWEEN 1900 AND 2100) AND (\"EndYear\" IS NULL OR \"EndYear\" BETWEEN 1900 AND 2100) AND (\"StartYear\" IS NULL OR \"EndYear\" IS NULL OR \"EndYear\" >= \"StartYear\") AND (NOT \"IsCurrentlyStudying\" OR \"EndYear\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_CareerConsultantEducation_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerConsultantExperience",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerConsultantExperience", x => x.Id);
                    table.CheckConstraint("CK_CareerConsultantExperience_Dates", "(\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\") AND (NOT \"IsCurrent\" OR \"EndDate\" IS NULL)");
                    table.CheckConstraint("CK_CareerConsultantExperience_Order", "\"DisplayOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_CareerConsultantExperience_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantEducation_ConsultantId_DisplayOrder",
                table: "CareerConsultantEducation",
                columns: new[] { "ConsultantId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantEducation_IsDeleted",
                table: "CareerConsultantEducation",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantExperience_ConsultantId_DisplayOrder",
                table: "CareerConsultantExperience",
                columns: new[] { "ConsultantId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantExperience_IsDeleted",
                table: "CareerConsultantExperience",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareerConsultantEducation");

            migrationBuilder.DropTable(
                name: "CareerConsultantExperience");

            migrationBuilder.DropColumn(
                name: "FunctionalArea",
                table: "CareerConsultants");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "CareerConsultants");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "CareerConsultants");

            migrationBuilder.DropColumn(
                name: "ProfessionalEmail",
                table: "CareerConsultants");

            migrationBuilder.DropColumn(
                name: "ProfileImageUrl",
                table: "CareerConsultants");

            migrationBuilder.DropColumn(
                name: "PublicProfileConsentAtUtc",
                table: "CareerConsultants");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "CareerConsultants");

            migrationBuilder.AlterColumn<decimal>(
                name: "YearsOfExperience",
                table: "CareerConsultants",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "TermsAcceptedAtUtc",
                table: "CareerConsultants",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ProfessionalType",
                table: "CareerConsultants",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
