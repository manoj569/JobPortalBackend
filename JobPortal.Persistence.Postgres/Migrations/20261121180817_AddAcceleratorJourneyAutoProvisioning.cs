using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceleratorJourneyAutoProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfigurationSource",
                table: "AcceleratorLearningPaths",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConfigurationSource",
                table: "AcceleratorCareerRoles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AcceleratorJourneyProvisioningReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketRoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarketSkillId = table.Column<Guid>(type: "uuid", nullable: true),
                    LearningPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    JourneyContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequiredCompetencies = table.Column<int>(type: "integer", nullable: false),
                    AssessedCompetencies = table.Column<int>(type: "integer", nullable: false),
                    UnassessedCompetencyIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceleratorJourneyProvisioningReceipts", x => x.Id);
                    table.CheckConstraint("CK_JourneyProvisioning_Coverage", "\"RequiredCompetencies\" > 0 AND \"AssessedCompetencies\" > 0 AND \"AssessedCompetencies\" <= \"RequiredCompetencies\"");
                    table.CheckConstraint("CK_JourneyProvisioning_Source", "(\"MarketRoleId\" IS NOT NULL AND \"MarketSkillId\" IS NULL) OR (\"MarketRoleId\" IS NULL AND \"MarketSkillId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AcceleratorJourneyProvisioningReceipts_AcceleratorJourneyCo~",
                        column: x => x.JourneyContextId,
                        principalTable: "AcceleratorJourneyContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorJourneyProvisioningReceipts_AcceleratorLearningP~",
                        column: x => x.LearningPathId,
                        principalTable: "AcceleratorLearningPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorJourneyProvisioningReceipts_AcceleratorPathEnrol~",
                        column: x => x.EnrollmentId,
                        principalTable: "AcceleratorPathEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorJourneyProvisioningReceipts_MarketRoles_MarketRo~",
                        column: x => x.MarketRoleId,
                        principalTable: "MarketRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorJourneyProvisioningReceipts_MarketSkills_MarketS~",
                        column: x => x.MarketSkillId,
                        principalTable: "MarketSkills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcceleratorJourneyProvisioningReceipts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyProvisioningReceipts_EnrollmentId",
                table: "AcceleratorJourneyProvisioningReceipts",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyProvisioningReceipts_IsDeleted",
                table: "AcceleratorJourneyProvisioningReceipts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyProvisioningReceipts_JourneyContextId",
                table: "AcceleratorJourneyProvisioningReceipts",
                column: "JourneyContextId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyProvisioningReceipts_LearningPathId",
                table: "AcceleratorJourneyProvisioningReceipts",
                column: "LearningPathId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyProvisioningReceipts_MarketRoleId",
                table: "AcceleratorJourneyProvisioningReceipts",
                column: "MarketRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyProvisioningReceipts_MarketSkillId",
                table: "AcceleratorJourneyProvisioningReceipts",
                column: "MarketSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceleratorJourneyProvisioningReceipts_UserId_RequestKey",
                table: "AcceleratorJourneyProvisioningReceipts",
                columns: new[] { "UserId", "RequestKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceleratorJourneyProvisioningReceipts");

            migrationBuilder.DropColumn(
                name: "ConfigurationSource",
                table: "AcceleratorLearningPaths");

            migrationBuilder.DropColumn(
                name: "ConfigurationSource",
                table: "AcceleratorCareerRoles");
        }
    }
}
