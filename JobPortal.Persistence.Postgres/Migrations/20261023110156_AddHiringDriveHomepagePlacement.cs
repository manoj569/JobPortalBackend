using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddHiringDriveHomepagePlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowOnHome",
                table: "HiringDrives",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_HiringDrives_ShowOnHome_IsActive_DisplayOrder",
                table: "HiringDrives",
                columns: new[] { "ShowOnHome", "IsActive", "DisplayOrder" },
                filter: "\"IsDeleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HiringDrives_ShowOnHome_IsActive_DisplayOrder",
                table: "HiringDrives");

            migrationBuilder.DropColumn(
                name: "ShowOnHome",
                table: "HiringDrives");
        }
    }
}
