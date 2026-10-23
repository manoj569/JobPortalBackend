using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddHiringDrives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HiringDrives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    BadgeText = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    BadgeType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LocationText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExperienceText = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApplyUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    StartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSponsored = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImpressionCount = table.Column<long>(type: "bigint", nullable: false),
                    ClickCount = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringDrives", x => x.Id);
                    table.CheckConstraint("CK_HiringDrives_Counts", "\"ClickCount\" >= 0 AND \"ImpressionCount\" >= 0");
                    table.CheckConstraint("CK_HiringDrives_Dates", "\"EndAtUtc\" IS NULL OR \"EndAtUtc\" > \"StartAtUtc\"");
                    table.CheckConstraint("CK_HiringDrives_Destination", "\"JobId\" IS NOT NULL OR \"ApplyUrl\" IS NOT NULL");
                    table.CheckConstraint("CK_HiringDrives_DisplayOrder", "\"DisplayOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_HiringDrives_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringDrives_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringDrives_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HiringDrives_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HiringDrives_CompanyId",
                table: "HiringDrives",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringDrives_CreatedByUserId",
                table: "HiringDrives",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringDrives_IsActive_DisplayOrder_CreatedAtUtc",
                table: "HiringDrives",
                columns: new[] { "IsActive", "DisplayOrder", "CreatedAtUtc" },
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_HiringDrives_IsActive_StartAtUtc_EndAtUtc",
                table: "HiringDrives",
                columns: new[] { "IsActive", "StartAtUtc", "EndAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringDrives_IsDeleted",
                table: "HiringDrives",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_HiringDrives_JobId",
                table: "HiringDrives",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringDrives_UpdatedByUserId",
                table: "HiringDrives",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HiringDrives");
        }
    }
}
