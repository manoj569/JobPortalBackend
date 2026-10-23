using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobPromotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BannerImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BannerImageContent = table.Column<byte[]>(type: "bytea", nullable: true),
                    BannerContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisplayPriority = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPromotions", x => x.Id);
                    table.CheckConstraint("CK_JobPromotions_Banner", "(\"BannerImageContent\" IS NULL AND \"BannerContentType\" IS NULL AND \"BannerImageUrl\" IS NULL) OR (\"BannerImageContent\" IS NOT NULL AND \"BannerContentType\" IN ('image/jpeg', 'image/png', 'image/webp') AND \"BannerImageUrl\" IS NOT NULL)");
                    table.CheckConstraint("CK_JobPromotions_BannerSize", "\"BannerImageContent\" IS NULL OR octet_length(\"BannerImageContent\") BETWEEN 1 AND 5242880");
                    table.CheckConstraint("CK_JobPromotions_Dates", "\"EndsAtUtc\" IS NULL OR \"StartsAtUtc\" IS NULL OR \"EndsAtUtc\" > \"StartsAtUtc\"");
                    table.CheckConstraint("CK_JobPromotions_DisplayPriority", "\"DisplayPriority\" >= 0");
                    table.ForeignKey(
                        name: "FK_JobPromotions_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobPromotions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobPromotions_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobPromotions_CreatedByUserId",
                table: "JobPromotions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPromotions_IsDeleted",
                table: "JobPromotions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_JobPromotions_IsEnabled_StartsAtUtc_EndsAtUtc_DisplayPriori~",
                table: "JobPromotions",
                columns: new[] { "IsEnabled", "StartsAtUtc", "EndsAtUtc", "DisplayPriority" },
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_JobPromotions_JobId",
                table: "JobPromotions",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobPromotions_UpdatedByUserId",
                table: "JobPromotions",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobPromotions");
        }
    }
}
