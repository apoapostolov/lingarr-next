using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(41)]
public class M0041_StripSubtitleHtml : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "strip_subtitle_html", value = "true" });
    }

    public override void Down()
    {
    }
}
