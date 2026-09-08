using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalJobSiteSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalJobSiteSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Site = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EncryptedStorageState = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncryptionPurposeVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequiresReauthentication = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalJobSiteSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalJobSiteSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalJobSiteSessions_ExpiresAtUtc",
                table: "ExternalJobSiteSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalJobSiteSessions_IsDeleted",
                table: "ExternalJobSiteSessions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalJobSiteSessions_Status",
                table: "ExternalJobSiteSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalJobSiteSessions_UserId_Site",
                table: "ExternalJobSiteSessions",
                columns: new[] { "UserId", "Site" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalJobSiteSessions");
        }
    }
}
