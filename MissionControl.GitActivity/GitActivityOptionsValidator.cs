using Microsoft.Extensions.Options;
using MissionControl.GitActivity.Storage.Sqlite;

namespace MissionControl.GitActivity;

internal sealed class GitActivityOptionsValidator
    : IValidateOptions<GitActivityOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        GitActivityOptions options)
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

        if (options.DefaultResultLimit <= 0)
        {
            failures.Add(
                "GitActivity:DefaultResultLimit must be greater than zero.");
        }

        if (options.MaxResultLimit <= 0)
        {
            failures.Add(
                "GitActivity:MaxResultLimit must be greater than zero.");
        }

        if (options.DefaultResultLimit > options.MaxResultLimit)
        {
            failures.Add(
                "GitActivity:DefaultResultLimit must not exceed MaxResultLimit.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey) ||
            options.ApiKey.Length < 32)
        {
            failures.Add(
                "GitActivity:ApiKey must contain at least 32 characters.");
        }

        if (options.AllowedRepositories is null ||
            options.AllowedRepositories.Length == 0)
        {
            failures.Add(
                "GitActivity:AllowedRepositories must contain at least one nonblank repository.");
        }
        else if (options.AllowedRepositories.Any(
                     string.IsNullOrWhiteSpace))
        {
            failures.Add(
                "GitActivity:AllowedRepositories contains a blank repository entry.");
        }

        if (options.AllowedBranches is null ||
            options.AllowedBranches.Length == 0)
        {
            failures.Add(
                "GitActivity:AllowedBranches must contain at least one nonblank branch.");
        }
        else if (options.AllowedBranches.Any(
                     string.IsNullOrWhiteSpace))
        {
            failures.Add(
                "GitActivity:AllowedBranches contains a blank branch entry.");
        }

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
                "GitActivity:DatabaseFileName is required.");
            return false;
        }

        if (!string.Equals(
                databaseFileName,
                databaseFileName.Trim(),
                StringComparison.Ordinal))
        {
            failures.Add(
                "GitActivity:DatabaseFileName must not have surrounding whitespace.");
            return false;
        }

        if (databaseFileName is "." or "..")
        {
            failures.Add(
                "GitActivity:DatabaseFileName must identify a file.");
            return false;
        }

        if (databaseFileName.Contains('/') ||
            databaseFileName.Contains('\\'))
        {
            failures.Add(
                "GitActivity:DatabaseFileName must be a filename without directory separators.");
            return false;
        }

        if (databaseFileName.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            failures.Add(
                "GitActivity:DatabaseFileName contains an invalid filename character.");
            return false;
        }

        return true;
    }

    private static void ValidateBasePath(
        GitActivityOptions options,
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
                "GitActivity:BasePath must not have surrounding whitespace.");
            return;
        }

        try
        {
            string basePath =
                GitActivityStoragePath.ResolveBasePath(
                    options.BasePath);

            if (File.Exists(basePath))
            {
                failures.Add(
                    "GitActivity:BasePath points to an existing file.");
                return;
            }

            if (databaseFileNameIsValid &&
                Directory.Exists(
                    GitActivityStoragePath.ResolveDatabasePath(
                        options)))
            {
                failures.Add(
                    "GitActivity:DatabaseFileName points to an existing directory.");
            }
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            failures.Add(
                "GitActivity:BasePath is not a valid filesystem path.");
        }
    }
}
