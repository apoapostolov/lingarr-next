using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(43)]
public class M0043_ClassifierProvider : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "classifier_provider", value = "jev" });
    }

    public override void Down()
    {
    }
}
