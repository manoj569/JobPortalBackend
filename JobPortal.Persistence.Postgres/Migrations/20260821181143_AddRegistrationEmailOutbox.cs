using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegistrationEmailRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationEmailRequests", x => x.Id);
                    table.CheckConstraint("CK_RegistrationEmailRequests_AttemptCount", "\"AttemptCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_RegistrationEmailRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailVerificationTokenHash",
                table: "Users",
                column: "EmailVerificationTokenHash",
                unique: true,
                filter: "\"EmailVerificationTokenHash\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationEmailRequests_IsDeleted",
                table: "RegistrationEmailRequests",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationEmailRequests_SentAtUtc_NextAttemptAtUtc_Locked~",
                table: "RegistrationEmailRequests",
                columns: new[] { "SentAtUtc", "NextAttemptAtUtc", "LockedUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationEmailRequests_UserId",
                table: "RegistrationEmailRequests",
                column: "UserId",
                filter: "\"SentAtUtc\" IS NULL AND \"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrationEmailRequests");

            migrationBuilder.DropIndex(
                name: "IX_Users_EmailVerificationTokenHash",
                table: "Users");
        }
    }
}
