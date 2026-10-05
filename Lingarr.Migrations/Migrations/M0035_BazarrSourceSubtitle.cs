using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(35)]
public class M0035_BazarrSourceSubtitle : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "bazarr_enabled", value = "false" });
        Insert.IntoTable("settings").Row(new { key = "bazarr_url", value = "" });
        Insert.IntoTable("settings").Row(new { key = "bazarr_api_key", value = "" });
        Insert.IntoTable("settings").Row(new { key = "bazarr_minimum_score", value = "70" });
    }

    public override void Down()
    {
    }
}
