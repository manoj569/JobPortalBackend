using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddHiringContactIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HiringContactDiscoveryLeases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringContactDiscoveryLeases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiringContactDiscoveryLeases_HiringOutreachSessions_OwnerSe~",
                        column: x => x.OwnerSessionId,
                        principalTable: "HiringOutreachSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HiringContactIntelligence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyDomain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    EmailHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedContact = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    JobFamily = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ContactType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Verification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FirstDiscoveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SuccessfulReuseCount = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringContactIntelligence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiringContactIntelligence_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HiringContactDiscoveryLeases_ExpiresAtUtc",
                table: "HiringContactDiscoveryLeases",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HiringContactDiscoveryLeases_IsDeleted",
                table: "HiringContactDiscoveryLeases",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_HiringContactDiscoveryLeases_OwnerSessionId",
                table: "HiringContactDiscoveryLeases",
                column: "OwnerSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringContactDiscoveryLeases_ScopeHash",
                table: "HiringContactDiscoveryLeases",
                column: "ScopeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringContactIntelligence_CompanyDomain_EmailHash",
                table: "HiringContactIntelligence",
                columns: new[] { "CompanyDomain", "EmailHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringContactIntelligence_CompanyDomain_JobFamily_Location",
                table: "HiringContactIntelligence",
                columns: new[] { "CompanyDomain", "JobFamily", "Location" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringContactIntelligence_CompanyId_CompanyDomain_IsActive_~",
                table: "HiringContactIntelligence",
                columns: new[] { "CompanyId", "CompanyDomain", "IsActive", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringContactIntelligence_IsDeleted",
                table: "HiringContactIntelligence",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HiringContactDiscoveryLeases");

            migrationBuilder.DropTable(
                name: "HiringContactIntelligence");
        }
    }
}
