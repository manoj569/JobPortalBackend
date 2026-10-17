using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ExtendCareerHarborPointsAndMentorEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserPointTransactions_HelpfulReward",
                table: "UserPointTransactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "InsightHelpfulnessFeedbackId",
                table: "UserPointTransactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "InterviewInsightId",
                table: "UserPointTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JobReferralId",
                table: "UserPointTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferralRequestId",
                table: "UserPointTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPointTransactions_InterviewInsightId",
                table: "UserPointTransactions",
                column: "InterviewInsightId",
                unique: true,
                filter: "\"InterviewInsightId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserPointTransactions_JobReferralId",
                table: "UserPointTransactions",
                column: "JobReferralId",
                unique: true,
                filter: "\"JobReferralId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserPointTransactions_ReferralRequestId",
                table: "UserPointTransactions",
                column: "ReferralRequestId",
                unique: true,
                filter: "\"ReferralRequestId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserPointTransactions_ValidReward",
                table: "UserPointTransactions",
                sql: "(\"Reason\" = 'InterviewInsightHelpful' AND \"Points\" = 10 AND \"InsightHelpfulnessFeedbackId\" IS NOT NULL AND \"InterviewInsightId\" IS NULL AND \"JobReferralId\" IS NULL AND \"ReferralRequestId\" IS NULL) OR (\"Reason\" = 'InterviewInsightPublished' AND \"Points\" = 5 AND \"InsightHelpfulnessFeedbackId\" IS NULL AND \"InterviewInsightId\" IS NOT NULL AND \"JobReferralId\" IS NULL AND \"ReferralRequestId\" IS NULL) OR (\"Reason\" = 'ReferralOpportunityShared' AND \"Points\" = 5 AND \"InsightHelpfulnessFeedbackId\" IS NULL AND \"InterviewInsightId\" IS NULL AND \"JobReferralId\" IS NOT NULL AND \"ReferralRequestId\" IS NULL) OR (\"Reason\" = 'ReferralCandidateConfirmed' AND \"Points\" = 300 AND \"InsightHelpfulnessFeedbackId\" IS NULL AND \"InterviewInsightId\" IS NULL AND \"JobReferralId\" IS NULL AND \"ReferralRequestId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_UserPointTransactions_InterviewInsights_InterviewInsightId",
                table: "UserPointTransactions",
                column: "InterviewInsightId",
                principalTable: "InterviewInsights",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPointTransactions_JobReferrals_JobReferralId",
                table: "UserPointTransactions",
                column: "JobReferralId",
                principalTable: "JobReferrals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPointTransactions_ReferralRequests_ReferralRequestId",
                table: "UserPointTransactions",
                column: "ReferralRequestId",
                principalTable: "ReferralRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserPointTransactions_InterviewInsights_InterviewInsightId",
                table: "UserPointTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPointTransactions_JobReferrals_JobReferralId",
                table: "UserPointTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPointTransactions_ReferralRequests_ReferralRequestId",
                table: "UserPointTransactions");

            migrationBuilder.DropIndex(
                name: "IX_UserPointTransactions_InterviewInsightId",
                table: "UserPointTransactions");

            migrationBuilder.DropIndex(
                name: "IX_UserPointTransactions_JobReferralId",
                table: "UserPointTransactions");

            migrationBuilder.DropIndex(
                name: "IX_UserPointTransactions_ReferralRequestId",
                table: "UserPointTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserPointTransactions_ValidReward",
                table: "UserPointTransactions");

            migrationBuilder.DropColumn(
                name: "InterviewInsightId",
                table: "UserPointTransactions");

            migrationBuilder.DropColumn(
                name: "JobReferralId",
                table: "UserPointTransactions");

            migrationBuilder.DropColumn(
                name: "ReferralRequestId",
                table: "UserPointTransactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "InsightHelpfulnessFeedbackId",
                table: "UserPointTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserPointTransactions_HelpfulReward",
                table: "UserPointTransactions",
                sql: "\"Points\" = 10 AND \"Reason\" = 'InterviewInsightHelpful'");
        }
    }
}
