namespace MissionControl.Archive.Storage.Sqlite;

public sealed class SqliteEventArchiveOptions
{
    public const string SectionName = "EventArchive";

    public string? DatabaseFileName { get; init; }

    public string? BasePath { get; init; }
}
