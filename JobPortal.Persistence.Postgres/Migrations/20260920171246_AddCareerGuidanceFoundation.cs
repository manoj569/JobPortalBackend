using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerGuidanceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CareerConsultants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProfessionalHeadline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Bio = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CurrentRole = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    YearsOfExperience = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    ProfessionalType = table.Column<int>(type: "integer", nullable: false),
                    LinkedInUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    VerificationStatus = table.Column<int>(type: "integer", nullable: false),
                    VerificationMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VerificationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TermsAcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerConsultants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareerConsultants_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerConsultants_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerConsultants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerConsultantServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerConsultantServices", x => x.Id);
                    table.CheckConstraint("CK_CareerConsultantServices_Duration", "\"DurationMinutes\" >= 15 AND \"DurationMinutes\" <= 180");
                    table.CheckConstraint("CK_CareerConsultantServices_Price", "\"Price\" > 0 AND \"Price\" <= 1000000");
                    table.ForeignKey(
                        name: "FK_CareerConsultantServices_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerConsultantTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerConsultantTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareerConsultantTags_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultants_CompanyId",
                table: "CareerConsultants",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultants_IsDeleted",
                table: "CareerConsultants",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultants_ProfessionalType_YearsOfExperience",
                table: "CareerConsultants",
                columns: new[] { "ProfessionalType", "YearsOfExperience" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultants_ReviewedByUserId",
                table: "CareerConsultants",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultants_UserId",
                table: "CareerConsultants",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultants_VerificationStatus_CreatedAtUtc_Id",
                table: "CareerConsultants",
                columns: new[] { "VerificationStatus", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantServices_ConsultantId_IsActive",
                table: "CareerConsultantServices",
                columns: new[] { "ConsultantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantServices_IsDeleted",
                table: "CareerConsultantServices",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantServices_ServiceType_Currency_Price",
                table: "CareerConsultantServices",
                columns: new[] { "ServiceType", "Currency", "Price" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantTags_ConsultantId_Kind_Value",
                table: "CareerConsultantTags",
                columns: new[] { "ConsultantId", "Kind", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantTags_IsDeleted",
                table: "CareerConsultantTags",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantTags_Kind_Value_ConsultantId",
                table: "CareerConsultantTags",
                columns: new[] { "Kind", "Value", "ConsultantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareerConsultantServices");

            migrationBuilder.DropTable(
                name: "CareerConsultantTags");

            migrationBuilder.DropTable(
                name: "CareerConsultants");
        }
    }
}
