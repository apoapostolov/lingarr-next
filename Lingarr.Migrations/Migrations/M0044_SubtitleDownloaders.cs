using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(44)]
public class M0044_SubtitleDownloaders : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "subtitle_downloaders_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "subtitle_download_providers", value = "[]" });
    }

    public override void Down()
    {
    }
}
