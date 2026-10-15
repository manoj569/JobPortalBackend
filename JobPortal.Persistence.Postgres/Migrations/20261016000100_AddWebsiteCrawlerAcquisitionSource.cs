using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Postgres.Migrations;

[DbContext(typeof(JobPortalDbContext))]
[Migration("20261016000100_AddWebsiteCrawlerAcquisitionSource")]
public sealed class AddWebsiteCrawlerAcquisitionSource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "AcquisitionSources",
            columns: ["Id", "ProviderKey", "Name", "IsEnabled", "AutoPublishEnabled", "CreatedAtUtc", "IsDeleted"],
            columnTypes: ["uuid", "character varying(100)", "character varying(160)", "boolean", "boolean", "timestamp with time zone", "boolean"],
            values: [new Guid("e7416f86-454a-4b59-9331-379956e36d92"), "website-crawler", "Generic Website Crawler", true, false, new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc), false]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(table: "AcquisitionSources", keyColumn: "Id", keyColumnType: "uuid",
            keyValue: new Guid("e7416f86-454a-4b59-9331-379956e36d92"));
    }
}
