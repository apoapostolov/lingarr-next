using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(38)]
public class M0038_PictureConvertLastResort : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "picture_convert_last_resort", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "picture_convert_wait_hours", value = "72" });
    }

    public override void Down()
    {
    }
}
