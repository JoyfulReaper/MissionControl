using MissionControl.Contracts.Archive;

namespace MissionControl.Client.Archive;

public sealed class EventFeedController(
    IArchiveEventClient client)
{
    public const int PageSize = 50;

    private readonly RefreshGate refreshGate = new();

    public IReadOnlyList<ArchiveEventSummaryItem> Events { get; private set; } = [];

    public IReadOnlyList<string> SourceSuggestions { get; private set; } = [];

    public IReadOnlyList<string> EventTypeSuggestions { get; private set; } = [];

    public string? SourceFilter { get; set; }

    public string? EventTypeFilter { get; set; }

    public Guid? SelectedEventId { get; private set; }

    public bool IsInitialLoading { get; private set; }

    public bool IsLoadingMore { get; private set; }

    public bool IsRefreshing => refreshGate.IsRunning;

    public bool HasMore { get; private set; }

    public bool HasOlderEventsLoaded { get; private set; }

    public bool NewEventsAvailable { get; private set; }

    public string? Error { get; private set; }

    public string? RefreshWarning { get; private set; }

    public string? LoadMoreError { get; private set; }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SourceFilter) ||
        !string.IsNullOrWhiteSpace(EventTypeFilter);

    public Task<bool> LoadInitialAsync(
        CancellationToken cancellationToken)
    {
        return LoadReplacingAsync(
            ignoreFilters: true,
            refreshSuggestions: true,
            cancellationToken);
    }

    public Task<bool> ApplyFiltersAsync(
        CancellationToken cancellationToken)
    {
        return LoadReplacingAsync(
            ignoreFilters: false,
            refreshSuggestions: false,
            cancellationToken);
    }

    public Task<bool> ClearFiltersAsync(
        CancellationToken cancellationToken)
    {
        SourceFilter = null;
        EventTypeFilter = null;

        return ApplyFiltersAsync(cancellationToken);
    }

    public async Task<bool> LoadOlderAsync(
        CancellationToken cancellationToken)
    {
        return await refreshGate.TryRunAsync(
            async token =>
            {
                IsLoadingMore = true;
                LoadMoreError = null;

                try
                {
                    ArchiveEventCursor? cursor =
                        Events.Count > 0
                            ? CreateCursor(Events[^1])
                            : null;
                    IReadOnlyList<ArchiveEventSummaryItem> result =
                        await QueryAsync(cursor, token);
                    ArchiveEventSummaryItem[] page =
                        result.Take(PageSize).ToArray();

                    Events = Events.Concat(page).ToArray();
                    HasMore = result.Count > PageSize;
                    HasOlderEventsLoaded =
                        HasOlderEventsLoaded || page.Length > 0;
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                    when (IsExpectedRefreshException(exception))
                {
                    LoadMoreError = CreateErrorMessage(exception);
                }
                finally
                {
                    IsLoadingMore = false;
                }
            },
            cancellationToken);
    }

    public async Task<bool> PollAsync(
        CancellationToken cancellationToken)
    {
        return await refreshGate.TryRunAsync(
            async token =>
            {
                try
                {
                    IReadOnlyList<ArchiveEventSummaryItem> result =
                        await QueryAsync(before: null, token);
                    ArchiveEventSummaryItem[] page =
                        result.Take(PageSize).ToArray();

                    if (HasOlderEventsLoaded)
                    {
                        HashSet<Guid> currentIds =
                            Events.Select(item => item.EventId).ToHashSet();

                        NewEventsAvailable =
                            NewEventsAvailable ||
                            page.Any(item =>
                                !currentIds.Contains(item.EventId));
                    }
                    else
                    {
                        Events = page;
                        HasMore = result.Count > PageSize;
                        NewEventsAvailable = false;
                    }

                    RefreshWarning = null;
                    Error = null;
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                    when (IsExpectedRefreshException(exception))
                {
                    RefreshWarning =
                        $"Latest event refresh failed: " +
                        CreateErrorMessage(exception);
                }
            },
            cancellationToken);
    }

    public Task<bool> LoadNewestAsync(
        CancellationToken cancellationToken)
    {
        return ApplyFiltersAsync(cancellationToken);
    }

    public void SelectEvent(Guid eventId)
    {
        SelectedEventId = eventId;
    }

    public void CloseSelectedEvent()
    {
        SelectedEventId = null;
    }

    private async Task<bool> LoadReplacingAsync(
        bool ignoreFilters,
        bool refreshSuggestions,
        CancellationToken cancellationToken)
    {
        return await refreshGate.TryRunAsync(
            async token =>
            {
                bool wasInitialLoad = Events.Count == 0 && Error is null;
                IsInitialLoading = wasInitialLoad;
                Error = null;
                LoadMoreError = null;

                try
                {
                    IReadOnlyList<ArchiveEventSummaryItem> result =
                        await QueryAsync(
                            before: null,
                            token,
                            ignoreFilters);
                    ArchiveEventSummaryItem[] page =
                        result.Take(PageSize).ToArray();

                    Events = page;
                    HasMore = result.Count > PageSize;
                    HasOlderEventsLoaded = false;
                    NewEventsAvailable = false;
                    RefreshWarning = null;

                    if (refreshSuggestions)
                    {
                        UpdateSuggestions(page);
                    }
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                    when (IsExpectedRefreshException(exception))
                {
                    string message = CreateErrorMessage(exception);

                    if (Events.Count == 0)
                    {
                        Error = message;
                    }
                    else
                    {
                        RefreshWarning =
                            $"Latest event refresh failed: {message}";
                    }
                }
                finally
                {
                    IsInitialLoading = false;
                }
            },
            cancellationToken);
    }

    private Task<IReadOnlyList<ArchiveEventSummaryItem>> QueryAsync(
        ArchiveEventCursor? before,
        CancellationToken cancellationToken,
        bool ignoreFilters = false)
    {
        return client.GetRecentAsync(
            limit: PageSize + 1,
            source: ignoreFilters
                ? null
                : NormalizeFilter(SourceFilter),
            eventType: ignoreFilters
                ? null
                : NormalizeFilter(EventTypeFilter),
            before: before,
            cancellationToken: cancellationToken);
    }

    private void UpdateSuggestions(
        IEnumerable<ArchiveEventSummaryItem> events)
    {
        SourceSuggestions = events
            .Select(item => item.Source)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        EventTypeSuggestions = events
            .Select(item => item.EventType)
            .Where(eventType => !string.IsNullOrWhiteSpace(eventType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(eventType => eventType, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static ArchiveEventCursor CreateCursor(
        ArchiveEventSummaryItem item)
    {
        return new ArchiveEventCursor(
            OccurredAt: item.OccurredAt,
            ReceivedAt: item.ReceivedAt,
            EventId: item.EventId);
    }

    private static bool IsExpectedRefreshException(
        Exception exception)
    {
        return exception is
            HttpRequestException or
            TaskCanceledException or
            InvalidOperationException;
    }

    private static string CreateErrorMessage(
        Exception exception)
    {
        return exception switch
        {
            HttpRequestException =>
                $"Mission Control Archive could not be reached: " +
                exception.Message,
            TaskCanceledException =>
                "The Mission Control Archive request timed out.",
            _ => exception.Message
        };
    }
}
