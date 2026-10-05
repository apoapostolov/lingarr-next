using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(34)]
public class M0034_JevClassifier : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "typesafe_api_key", value = "" });
        Insert.IntoTable("settings").Row(new { key = "jev_skip_non_dialogue", value = "false" });
        Insert.IntoTable("settings").Row(new { key = "jev_reject_untranslated", value = "false" });
    }

    public override void Down()
    {
    }
}
