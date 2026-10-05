using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(36)]
public class M0036_BazarrExtractFirst : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "bazarr_extract_first", value = "true" });
    }

    public override void Down()
    {
    }
}
