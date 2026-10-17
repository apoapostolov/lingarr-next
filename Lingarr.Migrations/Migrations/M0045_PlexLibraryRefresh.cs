using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(45)]
public class M0045_PlexLibraryRefresh : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "plex_library_refresh_keys", value = "[]" });
        Insert.IntoTable("settings").Row(new { key = "plex_library_refresh_schedule", value = "0 4 * * 0" });
    }

    public override void Down()
    {
    }
}
