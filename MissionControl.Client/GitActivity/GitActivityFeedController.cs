using MissionControl.Contracts.GitActivity;
using System.Net;

namespace MissionControl.Client.GitActivity;

public sealed class GitActivityFeedController(
    IGitActivityClient client)
{
    public const int ResultLimit = 50;

    private readonly GitActivityRefreshGate refreshGate = new();
    private IReadOnlyList<GitActivityItem> items = [];

    public IReadOnlyList<GitActivityItem> Items => items;

    public IReadOnlyList<GitActivityItem> FilteredItems =>
        items
            .Where(MatchesFilters)
            .ToArray();

    public IReadOnlyList<string> RepositorySuggestions { get; private set; } = [];

    public IReadOnlyList<string> BranchSuggestions { get; private set; } = [];

    public string? RepositoryFilter { get; set; }

    public string? BranchFilter { get; set; }

    public bool IsInitialLoading { get; private set; }

    public bool IsRefreshing => refreshGate.IsRunning;

    public bool HasLoaded { get; private set; }

    public string? Error { get; private set; }

    public string? RefreshWarning { get; private set; }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(RepositoryFilter) ||
        !string.IsNullOrWhiteSpace(BranchFilter);

    public Task<bool> LoadInitialAsync(
        CancellationToken cancellationToken)
    {
        return LoadAsync(initialLoad: true, cancellationToken);
    }

    public Task<bool> RefreshAsync(
        CancellationToken cancellationToken)
    {
        return LoadAsync(initialLoad: false, cancellationToken);
    }

    public void ClearFilters()
    {
        RepositoryFilter = null;
        BranchFilter = null;
    }

    private async Task<bool> LoadAsync(
        bool initialLoad,
        CancellationToken cancellationToken)
    {
        return await refreshGate.TryRunAsync(
            async token =>
            {
                IsInitialLoading = initialLoad && !HasLoaded;

                if (IsInitialLoading)
                {
                    Error = null;
                }

                try
                {
                    IReadOnlyList<GitActivityItem> result =
                        await client.GetRecentAsync(
                            ResultLimit,
                            token);

                    items = result
                        .OrderByDescending(item => item.Timestamp)
                        .ThenBy(item => item.Repository,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.Sha,
                            StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    UpdateSuggestions();
                    HasLoaded = true;
                    Error = null;
                    RefreshWarning = null;
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    string message = CreateErrorMessage(exception);

                    if (!HasLoaded)
                    {
                        Error = message;
                    }
                    else
                    {
                        RefreshWarning =
                            $"Latest Git Activity refresh failed: {message}";
                    }
                }
                finally
                {
                    IsInitialLoading = false;
                }
            },
            cancellationToken);
    }

    private bool MatchesFilters(GitActivityItem item)
    {
        return MatchesFilter(item.Repository, RepositoryFilter) &&
               MatchesFilter(item.Branch, BranchFilter);
    }

    private static bool MatchesFilter(
        string value,
        string? filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
               value.Contains(
                   filter.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSuggestions()
    {
        RepositorySuggestions = items
            .Select(item => item.Repository)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        BranchSuggestions = items
            .Select(item => item.Branch)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CreateErrorMessage(Exception exception)
    {
        return exception switch
        {
            HttpRequestException
            {
                StatusCode: HttpStatusCode.Unauthorized
            } =>
                "The Dashboard rejected the Mobile API token. " +
                "Update it in Settings.",

            HttpRequestException
            {
                StatusCode: HttpStatusCode.BadGateway
            } =>
                "The Dashboard could not reach Git Activity.",

            TaskCanceledException =>
                "The Git Activity request timed out.",

            _ =>
                "Git Activity could not be loaded. Try again."
        };
    }
}

internal sealed class GitActivityRefreshGate
{
    private int isRunning;

    public bool IsRunning =>
        Volatile.Read(ref isRunning) != 0;

    public async Task<bool> TryRunAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(
                ref isRunning,
                1,
                0) != 0)
        {
            return false;
        }

        try
        {
            await action(cancellationToken);
            return true;
        }
        finally
        {
            Volatile.Write(ref isRunning, 0);
        }
    }
}
