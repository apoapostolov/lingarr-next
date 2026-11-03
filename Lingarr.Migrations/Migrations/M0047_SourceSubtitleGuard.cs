using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(47)]
public class M0047_SourceSubtitleGuard : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "source_subtitle_guard", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "request_attempt_timeout", value = "120" });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "source_subtitle_guard" });
        Delete.FromTable("settings").Row(new { key = "request_attempt_timeout" });
    }
}
