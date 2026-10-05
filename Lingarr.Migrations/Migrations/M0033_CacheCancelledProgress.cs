using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(33)]
public class M0033_CacheCancelledProgress : Migration
{
    public override void Up()
    {
        Alter.Table("translation_requests")
            .AddColumn("cached_progress").AsInt32().Nullable();

        Insert.IntoTable("settings").Row(new
        {
            key = "cache_cancelled_progress",
            value = "true"
        });
        Insert.IntoTable("settings").Row(new
        {
            key = "cache_cancelled_quality_threshold",
            value = "90"
        });
    }

    public override void Down()
    {
    }
}
