using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableResumeDocumentStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResumeDocumentBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResumeId = table.Column<Guid>(type: "uuid", nullable: true),
                    StorageKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Extension = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    FileLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResumeDocumentBlobs", x => x.Id);
                    table.CheckConstraint("CK_ResumeDocumentBlobs_FileLength", "\"FileLength\" > 0 AND \"FileLength\" <= 10485760");
                    table.ForeignKey(
                        name: "FK_ResumeDocumentBlobs_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResumeDocumentBlobs_IsDeleted",
                table: "ResumeDocumentBlobs",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ResumeDocumentBlobs_OwnerUserId_ResumeId",
                table: "ResumeDocumentBlobs",
                columns: new[] { "OwnerUserId", "ResumeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResumeDocumentBlobs_OwnerUserId_StorageKey",
                table: "ResumeDocumentBlobs",
                columns: new[] { "OwnerUserId", "StorageKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ResumeDocumentBlobs_StorageKey",
                table: "ResumeDocumentBlobs",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResumeDocumentBlobs");
        }
    }
}
