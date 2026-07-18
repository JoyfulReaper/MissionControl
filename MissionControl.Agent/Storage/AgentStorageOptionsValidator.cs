using Microsoft.Extensions.Options;

namespace MissionControl.Agent.Storage;

internal sealed class AgentStorageOptionsValidator
    : IValidateOptions<AgentStorageOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AgentStorageOptions options)
    {
        List<string> failures = [];

        bool databaseFileNameIsValid =
            ValidateDatabaseFileName(
                options.DatabaseFileName,
                failures);
        ValidateBasePath(
            options,
            databaseFileNameIsValid,
            failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool ValidateDatabaseFileName(
        string? databaseFileName,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(databaseFileName))
        {
            failures.Add(
                "AgentStorage:DatabaseFileName is required.");
            return false;
        }

        if (!string.Equals(
                databaseFileName,
                databaseFileName.Trim(),
                StringComparison.Ordinal))
        {
            failures.Add(
                "AgentStorage:DatabaseFileName must not have surrounding whitespace.");
            return false;
        }

        if (databaseFileName is "." or "..")
        {
            failures.Add(
                "AgentStorage:DatabaseFileName must identify a file.");
            return false;
        }

        if (databaseFileName.Contains('/') ||
            databaseFileName.Contains('\\'))
        {
            failures.Add(
                "AgentStorage:DatabaseFileName must be a filename without directory separators.");
            return false;
        }

        if (databaseFileName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            failures.Add(
                "AgentStorage:DatabaseFileName contains an invalid filename character.");
            return false;
        }

        return true;
    }

    private static void ValidateBasePath(
        AgentStorageOptions options,
        bool databaseFileNameIsValid,
        List<string> failures)
    {
        if (!string.IsNullOrWhiteSpace(options.BasePath) &&
            !string.Equals(
                options.BasePath,
                options.BasePath.Trim(),
                StringComparison.Ordinal))
        {
            failures.Add(
                "AgentStorage:BasePath must not have surrounding whitespace.");
            return;
        }

        try
        {
            string basePath =
                AgentStoragePath.ResolveBasePath(options.BasePath);

            if (File.Exists(basePath))
            {
                failures.Add(
                    "AgentStorage:BasePath points to an existing file.");
                return;
            }

            if (databaseFileNameIsValid &&
                Directory.Exists(
                    AgentStoragePath.ResolveDatabasePath(options)))
            {
                failures.Add(
                    "AgentStorage:DatabaseFileName points to an existing directory.");
            }
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            failures.Add(
                "AgentStorage:BasePath is not a valid filesystem path.");
        }
    }
}
