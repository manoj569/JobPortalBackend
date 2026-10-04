using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerGuidancePaymentLifecycleFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CGSession_Status",
                table: "CareerGuidanceSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CGRefund_Status",
                table: "CareerGuidanceRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CareerBooking_Status",
                table: "CareerGuidanceBookings");



            migrationBuilder.AddColumn<DateTime>(
                name: "CandidateConfirmedAtUtc",
                table: "CareerGuidanceSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletionRequestedAtUtc",
                table: "CareerGuidanceSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyDecisionSnapshotJson",
                table: "CareerGuidanceRefunds",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptanceDueAtUtc",
                table: "CareerGuidanceBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsultantDecision",
                table: "CareerGuidanceBookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsultantDecisionAtUtc",
                table: "CareerGuidanceBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConsultantDecisionByUserId",
                table: "CareerGuidanceBookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LifecycleVersion",
                table: "CareerGuidanceBookings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "PolicySnapshotJson",
                table: "CareerGuidanceBookings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "CareerGuidanceBookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestPayloadHash",
                table: "CareerGuidanceBookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CareerConsultantPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EarningId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderPayoutId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ProcessingAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerConsultantPayouts", x => x.Id);
                    table.CheckConstraint("CK_CGPayout_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CGPayout_Status", "\"Status\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_CareerConsultantPayouts_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerConsultantPayouts_CareerGuidanceEarnings_EarningId",
                        column: x => x.EarningId,
                        principalTable: "CareerGuidanceEarnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CGSession_Status",
                table: "CareerGuidanceSessions",
                sql: "\"Status\" BETWEEN 1 AND 8");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CGRefund_Status",
                table: "CareerGuidanceRefunds",
                sql: "\"Status\" BETWEEN 1 AND 4 AND \"ReasonCode\" BETWEEN 1 AND 7");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceBookings_CandidateUserId_RequestId",
                table: "CareerGuidanceBookings",
                columns: new[] { "CandidateUserId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceBookings_ConsultantDecisionByUserId",
                table: "CareerGuidanceBookings",
                column: "ConsultantDecisionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceBookings_Status_AcceptanceDueAtUtc",
                table: "CareerGuidanceBookings",
                columns: new[] { "Status", "AcceptanceDueAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CareerBooking_ConsultantDecision",
                table: "CareerGuidanceBookings",
                sql: "\"ConsultantDecision\" IS NULL OR \"ConsultantDecision\" BETWEEN 1 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CareerBooking_LifecycleVersion",
                table: "CareerGuidanceBookings",
                sql: "\"LifecycleVersion\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CareerBooking_Status",
                table: "CareerGuidanceBookings",
                sql: "\"Status\" BETWEEN 1 AND 9");

            // AwaitingConsultant (9) represents a paid booking request.
            // It must reserve the consultant's slot exactly like Pending and Confirmed.
            migrationBuilder.Sql("""
    ALTER TABLE "CareerGuidanceBookings"
    DROP CONSTRAINT "EX_CareerGuidanceBookings_NoOverlap";

    ALTER TABLE "CareerGuidanceBookings"
    ADD CONSTRAINT "EX_CareerGuidanceBookings_NoOverlap"
    EXCLUDE USING gist (
        "ConsultantId" WITH =,
        tstzrange("StartUtc", "EndUtc", '[)') WITH &&
    )
    WHERE ("IsDeleted" = FALSE AND "Status" IN (1, 2, 9));
    """);

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantPayouts_ConsultantId_Status_CreatedAtUtc",
                table: "CareerConsultantPayouts",
                columns: new[] { "ConsultantId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantPayouts_EarningId",
                table: "CareerConsultantPayouts",
                column: "EarningId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantPayouts_IsDeleted",
                table: "CareerConsultantPayouts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantPayouts_ProviderPayoutId",
                table: "CareerConsultantPayouts",
                column: "ProviderPayoutId",
                unique: true,
                filter: "\"ProviderPayoutId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CareerConsultantPayouts_Status_CreatedAtUtc",
                table: "CareerConsultantPayouts",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_CareerGuidanceBookings_Users_ConsultantDecisionByUserId",
                table: "CareerGuidanceBookings",
                column: "ConsultantDecisionByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the legacy booking overlap rule.
            migrationBuilder.Sql("""
    ALTER TABLE "CareerGuidanceBookings"
    DROP CONSTRAINT "EX_CareerGuidanceBookings_NoOverlap";

    ALTER TABLE "CareerGuidanceBookings"
    ADD CONSTRAINT "EX_CareerGuidanceBookings_NoOverlap"
    EXCLUDE USING gist (
        "ConsultantId" WITH =,
        tstzrange("StartUtc", "EndUtc", '[)') WITH &&
    )
    WHERE ("IsDeleted" = FALSE AND "Status" IN (1, 2));
    """);
            migrationBuilder.DropForeignKey(
                name: "FK_CareerGuidanceBookings_Users_ConsultantDecisionByUserId",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropTable(
                name: "CareerConsultantPayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CGSession_Status",
                table: "CareerGuidanceSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CGRefund_Status",
                table: "CareerGuidanceRefunds");

            migrationBuilder.DropIndex(
                name: "IX_CareerGuidanceBookings_CandidateUserId_RequestId",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropIndex(
                name: "IX_CareerGuidanceBookings_ConsultantDecisionByUserId",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropIndex(
                name: "IX_CareerGuidanceBookings_Status_AcceptanceDueAtUtc",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CareerBooking_ConsultantDecision",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CareerBooking_LifecycleVersion",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CareerBooking_Status",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropColumn(
                name: "CandidateConfirmedAtUtc",
                table: "CareerGuidanceSessions");

            migrationBuilder.DropColumn(
                name: "CompletionRequestedAtUtc",
                table: "CareerGuidanceSessions");

            migrationBuilder.DropColumn(
                name: "PolicyDecisionSnapshotJson",
                table: "CareerGuidanceRefunds");

            migrationBuilder.DropColumn(
                name: "AcceptanceDueAtUtc",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropColumn(
                name: "ConsultantDecision",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropColumn(
                name: "ConsultantDecisionAtUtc",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropColumn(
                name: "ConsultantDecisionByUserId",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropColumn(
                name: "LifecycleVersion",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropColumn(
                name: "PolicySnapshotJson",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "CareerGuidanceBookings");

            migrationBuilder.DropColumn(
                name: "RequestPayloadHash",
                table: "CareerGuidanceBookings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CGSession_Status",
                table: "CareerGuidanceSessions",
                sql: "\"Status\" BETWEEN 1 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CGRefund_Status",
                table: "CareerGuidanceRefunds",
                sql: "\"Status\" BETWEEN 1 AND 4 AND \"ReasonCode\" BETWEEN 1 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CareerBooking_Status",
                table: "CareerGuidanceBookings",
                sql: "\"Status\" BETWEEN 1 AND 8");
        }
    }
}
