using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations;

[DbContext(typeof(JobPortalDbContext))]
[Migration("20260809090000_AddPasswordResetTokenHashIndex")]
public partial class AddPasswordResetTokenHashIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Users_PasswordResetTokenHash",
            table: "Users",
            column: "PasswordResetTokenHash",
            unique: true,
            filter: "[PasswordResetTokenHash] IS NOT NULL AND [IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Users_PasswordResetTokenHash",
            table: "Users");
    }
}
