using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCareerGuidanceReviewsDisputesAndTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CareerGuidanceDisputes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RequestedRefund = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Resolution = table.Column<int>(type: "integer", nullable: false),
                    AdminNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidanceDisputes", x => x.Id);
                    table.CheckConstraint("CK_CGDispute_Description", "length(btrim(\"Description\")) > 0");
                    table.CheckConstraint("CK_CGDispute_Enums", "\"Status\" BETWEEN 1 AND 7 AND \"Category\" BETWEEN 1 AND 8 AND \"Resolution\" BETWEEN 1 AND 8");
                    table.CheckConstraint("CK_CGDispute_Resolution", "(\"Status\" <= 4 AND \"Resolution\" = 1 AND \"ResolvedAtUtc\" IS NULL AND \"ResolvedByUserId\" IS NULL) OR (\"Status\" >= 5 AND \"Resolution\" > 1 AND \"ResolvedAtUtc\" IS NOT NULL AND \"ResolvedByUserId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CareerGuidanceDisputes_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceDisputes_CareerGuidanceBookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "CareerGuidanceBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceDisputes_CareerGuidancePayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CareerGuidancePayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceDisputes_CareerGuidanceSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "CareerGuidanceSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceDisputes_Users_CandidateUserId",
                        column: x => x.CandidateUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceDisputes_Users_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerGuidanceReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    ModerationStatus = table.Column<int>(type: "integer", nullable: false),
                    ModerationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidanceReviews", x => x.Id);
                    table.CheckConstraint("CK_CGReview_Moderation", "\"ModerationStatus\" BETWEEN 1 AND 4 AND (NOT \"IsPublished\" OR (\"ModerationStatus\" = 2 AND NOT \"IsDeleted\"))");
                    table.CheckConstraint("CK_CGReview_Rating", "\"Rating\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_CareerGuidanceReviews_CareerConsultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "CareerConsultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceReviews_CareerGuidanceBookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "CareerGuidanceBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceReviews_CareerGuidancePayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "CareerGuidancePayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceReviews_CareerGuidanceSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "CareerGuidanceSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceReviews_Users_CandidateUserId",
                        column: x => x.CandidateUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareerGuidanceDisputeEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisputeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsPrivateToAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerGuidanceDisputeEvidence", x => x.Id);
                    table.CheckConstraint("CK_CGEvidence_Description", "length(btrim(\"Description\")) > 0");
                    table.CheckConstraint("CK_CGEvidence_Type", "\"EvidenceType\" BETWEEN 1 AND 3 AND (\"EvidenceType\" <> 3 OR \"IsPrivateToAdmin\")");
                    table.ForeignKey(
                        name: "FK_CareerGuidanceDisputeEvidence_CareerGuidanceDisputes_Disput~",
                        column: x => x.DisputeId,
                        principalTable: "CareerGuidanceDisputes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareerGuidanceDisputeEvidence_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputeEvidence_DisputeId_RequestId",
                table: "CareerGuidanceDisputeEvidence",
                columns: new[] { "DisputeId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputeEvidence_IsDeleted",
                table: "CareerGuidanceDisputeEvidence",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputeEvidence_SubmittedByUserId",
                table: "CareerGuidanceDisputeEvidence",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputes_BookingId",
                table: "CareerGuidanceDisputes",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputes_CandidateUserId_Status_CreatedAtUtc",
                table: "CareerGuidanceDisputes",
                columns: new[] { "CandidateUserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputes_ConsultantId_Status_CreatedAtUtc",
                table: "CareerGuidanceDisputes",
                columns: new[] { "ConsultantId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputes_IsDeleted",
                table: "CareerGuidanceDisputes",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputes_PaymentId_Status",
                table: "CareerGuidanceDisputes",
                columns: new[] { "PaymentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputes_ResolvedByUserId",
                table: "CareerGuidanceDisputes",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputes_SessionId",
                table: "CareerGuidanceDisputes",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceDisputes_Status_CreatedAtUtc",
                table: "CareerGuidanceDisputes",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceReviews_BookingId",
                table: "CareerGuidanceReviews",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceReviews_CandidateUserId",
                table: "CareerGuidanceReviews",
                column: "CandidateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceReviews_ConsultantId_ModerationStatus_Created~",
                table: "CareerGuidanceReviews",
                columns: new[] { "ConsultantId", "ModerationStatus", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceReviews_IsDeleted",
                table: "CareerGuidanceReviews",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceReviews_PaymentId",
                table: "CareerGuidanceReviews",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CareerGuidanceReviews_SessionId",
                table: "CareerGuidanceReviews",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareerGuidanceDisputeEvidence");

            migrationBuilder.DropTable(
                name: "CareerGuidanceReviews");

            migrationBuilder.DropTable(
                name: "CareerGuidanceDisputes");
        }
    }
}
