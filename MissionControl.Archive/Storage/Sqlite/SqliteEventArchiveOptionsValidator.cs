using Microsoft.Extensions.Options;

namespace MissionControl.Archive.Storage.Sqlite;

internal sealed class SqliteEventArchiveOptionsValidator
    : IValidateOptions<SqliteEventArchiveOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        SqliteEventArchiveOptions options)
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
                "EventArchive:DatabaseFileName is required.");
            return false;
        }

        if (!string.Equals(
                databaseFileName,
                databaseFileName.Trim(),
                StringComparison.Ordinal))
        {
            failures.Add(
                "EventArchive:DatabaseFileName must not have surrounding whitespace.");
            return false;
        }

        if (databaseFileName is "." or "..")
        {
            failures.Add(
                "EventArchive:DatabaseFileName must identify a file.");
            return false;
        }

        if (databaseFileName.Contains('/') ||
            databaseFileName.Contains('\\'))
        {
            failures.Add(
                "EventArchive:DatabaseFileName must be a filename without directory separators.");
            return false;
        }

        if (databaseFileName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            failures.Add(
                "EventArchive:DatabaseFileName contains an invalid filename character.");
            return false;
        }

        return true;
    }

    private static void ValidateBasePath(
        SqliteEventArchiveOptions options,
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
                "EventArchive:BasePath must not have surrounding whitespace.");
            return;
        }

        try
        {
            string basePath =
                SqliteEventArchivePath.ResolveBasePath(
                    options.BasePath);

            if (File.Exists(basePath))
            {
                failures.Add(
                    "EventArchive:BasePath points to an existing file.");
                return;
            }

            if (databaseFileNameIsValid &&
                Directory.Exists(
                    SqliteEventArchivePath.ResolveDatabasePath(
                        options)))
            {
                failures.Add(
                    "EventArchive:DatabaseFileName points to an existing directory.");
            }
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            failures.Add(
                "EventArchive:BasePath is not a valid filesystem path.");
        }
    }
}
