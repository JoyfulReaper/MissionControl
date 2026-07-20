using MissionControl.Client.Archive;
using MissionControl.Contracts.Archive;

using MissionControl.Client.GitActivity;
using MissionControl.Contracts.GitActivity;

namespace MissionControl.Dashboard.MobileApi;

public static class MobileApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMobileApiEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapGet(
                "/api/mobile/ping",
                static (HttpResponse response) =>
                {
                    DisableResponseCaching(response);

                    return Results.Ok(
                        new
                        {
                            status = "ok"
                        });
                })
            .WithName("GetMobileApiPing")
            .WithTags("Mobile API")
            .RequireAuthorization(
                MobileApiAuthenticationDefaults.Policy);

        RouteGroupBuilder eventApi =
            endpoints
                .MapGroup("/api/events")
                .WithTags("Mobile API", "Events")
                .RequireAuthorization(
                    MobileApiAuthenticationDefaults.Policy);

        eventApi
            .MapGet(
                "/feed",
                HandleGetEventFeedAsync)
            .WithName("GetMobileApiEventFeed");

        eventApi
            .MapGet(
                "/statistics",
                HandleGetEventStatisticsAsync)
            .WithName("GetMobileApiEventStatistics");

        eventApi
            .MapGet(
                "/{eventId:guid}",
                HandleGetEventByIdAsync)
            .WithName("GetMobileApiEventById");

        endpoints
            .MapGet(
                "/api/mobile/git-activity",
                HandleGetGitActivityAsync)
            .WithName("GetMobileApiGitActivity")
            .WithTags("Mobile API", "Git Activity")
            .Produces<IReadOnlyList<GitActivityItem>>(
                StatusCodes.Status200OK)
            .ProducesProblem(
                StatusCodes.Status502BadGateway)
            .RequireAuthorization(
                MobileApiAuthenticationDefaults.Policy);

        return endpoints;
    }

    private static async Task<IResult> HandleGetGitActivityAsync(
        int? limit,
        IGitActivityClient gitActivityClient,
        HttpResponse response,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            IReadOnlyList<GitActivityItem> activity =
                await gitActivityClient.GetRecentAsync(
                    Math.Clamp(limit ?? 25, 1, 50),
                    cancellationToken);

            return Results.Ok(activity);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedGitActivityException(exception))
        {
            return CreateGitActivityUnavailableResult(exception);
        }
        catch (Exception exception)
        {
            ILogger logger = loggerFactory.CreateLogger(
                "MissionControl.Dashboard.MobileApi.GitActivity");

            logger.LogError(
                exception,
                "Unexpected Git Activity proxy failure.");

            return CreateGitActivityUnavailableResult(exception);
        }
    }

    private static async Task<IResult> HandleGetEventFeedAsync(
        int? limit,
        string? source,
        string? eventType,
        DateTimeOffset? beforeOccurredAt,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeEventId,
        IArchiveEventClient archiveClient,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        bool hasAnyCursorValue =
            beforeOccurredAt is not null ||
            beforeReceivedAt is not null ||
            beforeEventId is not null;

        bool hasCompleteCursor =
            beforeOccurredAt is not null &&
            beforeReceivedAt is not null &&
            beforeEventId is not null;

        if (hasAnyCursorValue && !hasCompleteCursor)
        {
            return Results.Problem(
                title: "Invalid event cursor.",
                detail:
                    "beforeOccurredAt, beforeReceivedAt, and " +
                    "beforeEventId must be provided together.",
                statusCode:
                    StatusCodes.Status400BadRequest);
        }

        ArchiveEventCursor? cursor =
            hasCompleteCursor
                ? new ArchiveEventCursor(
                    OccurredAt: beforeOccurredAt!.Value,
                    ReceivedAt: beforeReceivedAt!.Value,
                    EventId: beforeEventId!.Value)
                : null;

        try
        {
            IReadOnlyList<ArchiveEventSummaryItem> events =
                await archiveClient.GetRecentAsync(
                    limit: Math.Clamp(limit ?? 50, 1, 200),
                    source: source,
                    eventType: eventType,
                    before: cursor,
                    cancellationToken: cancellationToken);

            return Results.Ok(events);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedArchiveException(exception))
        {
            return CreateArchiveUnavailableResult(exception);
        }
    }

    private static async Task<IResult>
        HandleGetEventStatisticsAsync(
            IArchiveEventClient archiveClient,
            HttpResponse response,
            CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            ArchiveStatisticsItem statistics =
                await archiveClient.GetStatisticsAsync(
                    cancellationToken);

            return Results.Ok(statistics);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedArchiveException(exception))
        {
            return CreateArchiveUnavailableResult(exception);
        }
    }

    private static async Task<IResult> HandleGetEventByIdAsync(
        Guid eventId,
        IArchiveEventClient archiveClient,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            ArchiveEventDetailsItem? archivedEvent =
                await archiveClient.GetByIdAsync(
                    eventId,
                    cancellationToken);

            return archivedEvent is null
                ? Results.NotFound()
                : Results.Ok(archivedEvent);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedArchiveException(exception))
        {
            return CreateArchiveUnavailableResult(exception);
        }
    }

    private static bool IsExpectedArchiveException(
        Exception exception)
    {
        return exception is
            HttpRequestException or
            TaskCanceledException or
            InvalidOperationException;
    }

    private static bool IsExpectedGitActivityException(
        Exception exception)
    {
        return exception is
            HttpRequestException or
            TaskCanceledException or
            InvalidOperationException;
    }

    private static IResult CreateGitActivityUnavailableResult(
        Exception exception)
    {
        string detail =
            exception switch
            {
                TaskCanceledException =>
                    "The Git Activity request timed out.",

                HttpRequestException =>
                    "Git Activity could not be reached.",

                _ =>
                    "The Git Activity request failed."
            };

        return Results.Problem(
            title: "Git Activity unavailable.",
            detail: detail,
            statusCode:
                StatusCodes.Status502BadGateway);
    }

    private static IResult CreateArchiveUnavailableResult(
        Exception exception)
    {
        string detail =
            exception switch
            {
                TaskCanceledException =>
                    "The Mission Control Archive request timed out.",

                HttpRequestException =>
                    "The Mission Control Archive could not be reached.",

                _ =>
                    "The Mission Control Archive request failed."
            };

        return Results.Problem(
            title: "Mission Control Archive unavailable.",
            detail: detail,
            statusCode:
                StatusCodes.Status502BadGateway);
    }

    private static void DisableResponseCaching(
        HttpResponse response)
    {
        response.Headers["Cache-Control"] =
            "no-store";

        response.Headers["Pragma"] =
            "no-cache";
    }
}
