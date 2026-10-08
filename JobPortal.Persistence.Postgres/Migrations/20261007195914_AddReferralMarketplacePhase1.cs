using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralMarketplacePhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReferralSlots",
                table: "JobReferrals",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ReferralRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobReferralId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferrerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferralSubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CandidateConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CandidateMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReferralSubmissionReference = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    AcceptedMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuotaPeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QuotaPeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralRequests", x => x.Id);
                    table.CheckConstraint("CK_ReferralRequests_Acceptance", "(\"Status\" IN (1, 3, 4) AND \"AcceptedAtUtc\" IS NULL AND \"AcceptedMembershipId\" IS NULL AND \"QuotaPeriodStartUtc\" IS NULL AND \"QuotaPeriodEndUtc\" IS NULL) OR (\"Status\" IN (2, 5, 6) AND \"AcceptedAtUtc\" IS NOT NULL AND \"AcceptedMembershipId\" IS NOT NULL AND \"QuotaPeriodStartUtc\" IS NOT NULL AND \"QuotaPeriodEndUtc\" IS NOT NULL AND \"QuotaPeriodEndUtc\" > \"QuotaPeriodStartUtc\")");
                    table.CheckConstraint("CK_ReferralRequests_Expiry", "\"ExpiresAtUtc\" = \"RequestedAtUtc\" + INTERVAL '48 hours'");
                    table.CheckConstraint("CK_ReferralRequests_Status", "\"Status\" BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "FK_ReferralRequests_JobReferrals_JobReferralId",
                        column: x => x.JobReferralId,
                        principalTable: "JobReferrals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralRequests_Memberships_AcceptedMembershipId",
                        column: x => x.AcceptedMembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralRequests_Users_CandidateUserId",
                        column: x => x.CandidateUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralRequests_Users_ReferrerUserId",
                        column: x => x.ReferrerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobReferrals_Slots",
                table: "JobReferrals",
                sql: "\"ReferralSlots\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRequests_AcceptedMembershipId",
                table: "ReferralRequests",
                column: "AcceptedMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRequests_CandidateUserId_AcceptedMembershipId_Quota~",
                table: "ReferralRequests",
                columns: new[] { "CandidateUserId", "AcceptedMembershipId", "QuotaPeriodStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRequests_CandidateUserId_JobReferralId",
                table: "ReferralRequests",
                columns: new[] { "CandidateUserId", "JobReferralId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRequests_CandidateUserId_Status",
                table: "ReferralRequests",
                columns: new[] { "CandidateUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRequests_IsDeleted",
                table: "ReferralRequests",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRequests_JobReferralId_Status",
                table: "ReferralRequests",
                columns: new[] { "JobReferralId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralRequests_ReferrerUserId_Status",
                table: "ReferralRequests",
                columns: new[] { "ReferrerUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReferralRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobReferrals_Slots",
                table: "JobReferrals");

            migrationBuilder.DropColumn(
                name: "ReferralSlots",
                table: "JobReferrals");
        }
    }
}
