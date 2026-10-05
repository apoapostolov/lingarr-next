using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(40)]
public class M0040_BazarrReplaceOcr : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "bazarr_replace_ocr", value = "true" });
    }

    public override void Down()
    {
    }
}
