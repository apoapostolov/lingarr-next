using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(39)]
public class M0039_BazarrRetry : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "bazarr_retry_hours", value = "12" });
        Insert.IntoTable("settings").Row(new { key = "bazarr_retry_timeout_hours", value = "168" });
    }

    public override void Down()
    {
    }
}
