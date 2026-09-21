using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerGuidancePaymentsAndEarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresPayment",
                table: "CareerGuidanceBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CareerGuidancePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AmountGross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PlatformCommissionPercentSnapshot = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    PlatformCommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ConsultantNetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequiresRefundReview = table.Column<bool>(type: "boolean", nullable: false),
                    RefundPolicyVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidancePayments", x => x.Id);
                    table.CheckConstraint("CK_CGPayment_Amounts", "\"AmountGross\" > 0 AND \"PlatformCommissionAmount\" >= 0 AND \"ConsultantNetAmount\" >= 0 AND \"AmountGross\" = \"PlatformCommissionAmount\" + \"ConsultantNetAmount\"");
                    table.CheckConstraint("CK_CGPayment_Commission", "\"PlatformCommissionPercentSnapshot\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_CGPayment_Status", "\"Status\" BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "FK_CareerGuidancePayments_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidancePayments_CareerGuidanceBookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "CareerGuidanceBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidancePayments_Users_CandidateUserId",
                        column: x => x.CandidateUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerGuidanceEarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformCommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SettlementReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidanceEarnings", x => x.Id);
                    table.CheckConstraint("CK_CGEarning_Amounts", "\"GrossAmount\" > 0 AND \"PlatformCommissionAmount\" >= 0 AND \"NetAmount\" >= 0 AND \"GrossAmount\" = \"PlatformCommissionAmount\" + \"NetAmount\"");
                    table.CheckConstraint("CK_CGEarning_Status", "\"Status\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_CareerGuidanceEarnings_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceEarnings_CareerGuidanceBookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "CareerGuidanceBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceEarnings_CareerGuidancePayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CareerGuidancePayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerGuidancePaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidancePaymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareerGuidancePaymentEvents_CareerGuidancePayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CareerGuidancePayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerGuidanceRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReasonCode = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderRefundId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidanceRefunds", x => x.Id);
                    table.CheckConstraint("CK_CGRefund_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CGRefund_Status", "\"Status\" BETWEEN 1 AND 4 AND \"ReasonCode\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_CareerGuidanceRefunds_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceRefunds_CareerGuidanceBookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "CareerGuidanceBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceRefunds_CareerGuidancePayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CareerGuidancePayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceRefunds_Users_CandidateUserId",
                        column: x => x.CandidateUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceRefunds_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceEarnings_BookingId",
                table: "CareerGuidanceEarnings",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceEarnings_ConsultantId_Status_CreatedAtUtc",
                table: "CareerGuidanceEarnings",
                columns: new[] { "ConsultantId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceEarnings_IsDeleted",
                table: "CareerGuidanceEarnings",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceEarnings_PaymentId",
                table: "CareerGuidanceEarnings",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceEarnings_Status_CreatedAtUtc",
                table: "CareerGuidanceEarnings",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePaymentEvents_EventKey",
                table: "CareerGuidancePaymentEvents",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePaymentEvents_IsDeleted",
                table: "CareerGuidancePaymentEvents",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePaymentEvents_PaymentId",
                table: "CareerGuidancePaymentEvents",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePayments_BookingId",
                table: "CareerGuidancePayments",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePayments_CandidateUserId_Status_CreatedAtUtc",
                table: "CareerGuidancePayments",
                columns: new[] { "CandidateUserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePayments_ConsultantId",
                table: "CareerGuidancePayments",
                column: "ConsultantId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePayments_IsDeleted",
                table: "CareerGuidancePayments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePayments_ProviderOrderId",
                table: "CareerGuidancePayments",
                column: "ProviderOrderId",
                unique: true,
                filter: "\"ProviderOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePayments_ProviderPaymentId",
                table: "CareerGuidancePayments",
                column: "ProviderPaymentId",
                unique: true,
                filter: "\"ProviderPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidancePayments_Status_RequiresRefundReview_CreatedA~",
                table: "CareerGuidancePayments",
                columns: new[] { "Status", "RequiresRefundReview", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceRefunds_BookingId",
                table: "CareerGuidanceRefunds",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceRefunds_CandidateUserId_Status_CreatedAtUtc",
                table: "CareerGuidanceRefunds",
                columns: new[] { "CandidateUserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceRefunds_ConsultantId",
                table: "CareerGuidanceRefunds",
                column: "ConsultantId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceRefunds_IsDeleted",
                table: "CareerGuidanceRefunds",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceRefunds_PaymentId",
                table: "CareerGuidanceRefunds",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceRefunds_ProviderRefundId",
                table: "CareerGuidanceRefunds",
                column: "ProviderRefundId",
                unique: true,
                filter: "\"ProviderRefundId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceRefunds_RequestedByUserId",
                table: "CareerGuidanceRefunds",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceRefunds_Status_CreatedAtUtc",
                table: "CareerGuidanceRefunds",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareerGuidanceEarnings");

            migrationBuilder.DropTable(
                name: "CareerGuidancePaymentEvents");

            migrationBuilder.DropTable(
                name: "CareerGuidanceRefunds");

            migrationBuilder.DropTable(
                name: "CareerGuidancePayments");

            migrationBuilder.DropColumn(
                name: "RequiresPayment",
                table: "CareerGuidanceBookings");
        }
    }
}
