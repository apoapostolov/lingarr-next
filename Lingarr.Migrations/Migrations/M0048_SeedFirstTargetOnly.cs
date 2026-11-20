using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(48)]
public class M0048_SeedFirstTargetOnly : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "first_target_only", value = "false" });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "first_target_only" });
    }
}
