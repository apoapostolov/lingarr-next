using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(6)]
public class M0006_AddTranslationRequestMediaId : Migration
{
    public override void Up()
    {
        if (!Schema.Table("translation_requests").Column("media_id").Exists())
        {
            Alter.Table("translation_requests")
                .AddColumn("media_id").AsInt32().Nullable();
        }
    }

    public override void Down()
    {
        // media_id is part of the request identity: dropping it on rollback would orphan
        // translation history. Upstream 1.5.0 keeps the column for the same reason.
    }
}
