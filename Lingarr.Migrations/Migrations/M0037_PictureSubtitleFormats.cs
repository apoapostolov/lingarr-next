using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(37)]
public class M0037_PictureSubtitleFormats : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "picture_ocr_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "caption_extract_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "nontext_pgs_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "nontext_vobsub_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "nontext_dvb_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "nontext_xsub_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "nontext_eia608_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "nontext_eia708_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "nontext_teletext_enabled", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "picture_scan_status", value = "idle" });
        Insert.IntoTable("settings").Row(new { key = "picture_scan_summary", value = "" });
    }

    public override void Down()
    {
    }
}
