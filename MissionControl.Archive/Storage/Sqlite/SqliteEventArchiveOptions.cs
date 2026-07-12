namespace MissionControl.Archive.Storage.Sqlite;

public sealed class SqliteEventArchiveOptions
{
    public const string SectionName = "EventArchive";

    public string DatabaseFileName { get; set; } =
        "mission-control.db";

    public string? BasePath { get; set; }
}