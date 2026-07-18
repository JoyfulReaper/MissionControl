namespace MissionControl.Agent.Storage;

internal static class AgentStoragePath
{
    private const string DefaultDirectoryName = "Data";

    internal static string ResolveBasePath(string? configuredBasePath)
    {
        if (string.IsNullOrWhiteSpace(configuredBasePath))
        {
            return Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    DefaultDirectoryName));
        }

        return Path.IsPathFullyQualified(configuredBasePath)
            ? Path.GetFullPath(configuredBasePath)
            : Path.GetFullPath(
                configuredBasePath,
                AppContext.BaseDirectory);
    }

    internal static string ResolveDatabasePath(
        AgentStorageOptions options)
    {
        return Path.GetFullPath(
            Path.Combine(
                ResolveBasePath(options.BasePath),
                options.DatabaseFileName!));
    }
}
