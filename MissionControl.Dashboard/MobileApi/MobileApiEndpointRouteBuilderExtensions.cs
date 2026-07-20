using MissionControl.Client.Archive;
using MissionControl.Contracts.Archive;

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

        return endpoints;
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

    private static IResult CreateArchiveUnavailableResult(
        Exception exception)
    {
        string detail =
            exception switch
            {
                TaskCanceledException =>
                    "The Mission Control Archive request timed out.",

                HttpRequestException =>
                    "The Mission Control Archive could not be reached: " +
                    exception.Message,

                _ => exception.Message
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