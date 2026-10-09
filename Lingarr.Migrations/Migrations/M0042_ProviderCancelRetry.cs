using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(42)]
public class M0042_ProviderCancelRetry : Migration
{
    public override void Up()
    {
        Alter.Table("translation_requests")
            .AddColumn("provider_cancel_attempts").AsInt32().NotNullable().WithDefaultValue(0);
        Alter.Table("translation_requests")
            .AddColumn("provider_cancel_retry_max").AsInt32().NotNullable().WithDefaultValue(0);
        Alter.Table("translation_requests")
            .AddColumn("provider_cancel_retry_pending").AsBoolean().NotNullable().WithDefaultValue(false);

        Insert.IntoTable("settings").Row(new { key = "provider_cancel_retry_count", value = "5" });
        Insert.IntoTable("settings").Row(new { key = "provider_cancel_retry_hours", value = "2" });
    }

    public override void Down()
    {
    }
}
