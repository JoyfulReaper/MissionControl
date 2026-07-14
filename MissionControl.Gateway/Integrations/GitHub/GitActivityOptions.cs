namespace Kgivler.Api.GitActivity;

public sealed class GitActivityOptions
{
    public const string SectionName = "GitActivity";

    public required string ArchiveBaseUrl { get; init; }

    public int CacheSeconds { get; init; } = 30;

    public int ArchiveQueryLimit { get; init; } = 50;

    public int PublicResultLimit { get; init; } = 10;

    public string[] AllowedRepositories { get; init; } = [];

    public string[] AllowedBranches { get; init; } = ["main"];
}