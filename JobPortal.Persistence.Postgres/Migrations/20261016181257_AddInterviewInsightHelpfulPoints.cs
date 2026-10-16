using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewInsightHelpfulPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPointTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InsightHelpfulnessFeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPointTransactions", x => x.Id);
                    table.CheckConstraint("CK_UserPointTransactions_HelpfulReward", "\"Points\" = 10 AND \"Reason\" = 'InterviewInsightHelpful'");
                    table.ForeignKey(
                        name: "FK_UserPointTransactions_InsightHelpfulnessFeedback_InsightHel~",
                        column: x => x.InsightHelpfulnessFeedbackId,
                        principalTable: "InsightHelpfulnessFeedback",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPointTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPointTransactions_InsightHelpfulnessFeedbackId",
                table: "UserPointTransactions",
                column: "InsightHelpfulnessFeedbackId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPointTransactions_UserId_CreatedAtUtc",
                table: "UserPointTransactions",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPointTransactions");
        }
    }
}
