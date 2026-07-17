/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using MissionControl.Archive.Contracts;
using MissionControl.Archive.Storage;

namespace MissionControl.Archive.Endpoints;

public static class ArchiveEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapArchiveEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/events",
                HandleGetRecentEventsAsync)
            .WithName("GetRecentEvents")
            .WithTags("Events");

        endpoints
            .MapGet(
                "/api/events/feed",
                HandleGetEventFeedAsync)
            .WithName("GetEventFeed")
            .WithTags("Events");

        endpoints
            .MapGet(
                "/api/events/{eventId:guid}",
                HandleGetEventByIdAsync)
            .WithName("GetEventById")
            .WithTags("Events");

        endpoints
            .MapGet(
                "/api/events/statistics",
                HandleGetEventStatisticsAsync)
            .WithName("GetEventStatistics")
            .WithTags("Events");

        return endpoints;
    }

    private static async Task<IResult>
            HandleGetEventStatisticsAsync(
                IIntegrationEventQuery query,
                CancellationToken cancellationToken)
    {
        DateTimeOffset receivedSince =
            DateTimeOffset.UtcNow.AddHours(-24);

        EventArchiveStatistics statistics =
            await query.GetStatisticsAsync(
                receivedSince: receivedSince,
                topCategoryLimit: 5,
                cancellationToken: cancellationToken);

        return Results.Ok(statistics);
    }

    private static async Task<IResult> HandleGetRecentEventsAsync(
        int? limit,
        string? source,
        string? eventType,
        DateTimeOffset? before,
        IIntegrationEventQuery query,
        CancellationToken cancellationToken)
    {
        int effectiveLimit =
            Math.Clamp(limit ?? 50, 1, 200);

        var events = await query.GetRecentAsync(
            effectiveLimit,
            source,
            eventType,
            before,
            cancellationToken);

        return Results.Ok(events);
    }

    private static async Task<IResult> HandleGetEventFeedAsync(
        int? limit,
        string? source,
        string? eventType,
        DateTimeOffset? beforeOccurredAt,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeEventId,
        IIntegrationEventQuery query,
        CancellationToken cancellationToken)
    {
        int effectiveLimit =
            Math.Clamp(limit ?? 50, 1, 200);

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
            return Results.BadRequest(
                "beforeOccurredAt, beforeReceivedAt, and " +
                "beforeEventId must be provided together.");
        }

        var events = await query.GetRecentSummariesAsync(
            limit: effectiveLimit,
            source: source,
            eventType: eventType,
            beforeOccurredAt: beforeOccurredAt,
            beforeReceivedAt: beforeReceivedAt,
            beforeEventId: beforeEventId,
            cancellationToken: cancellationToken);

        return Results.Ok(events);
    }

    private static async Task<IResult> HandleGetEventByIdAsync(
        Guid eventId,
        IIntegrationEventQuery query,
        CancellationToken cancellationToken)
    {
        var archivedEvent = await query.GetByIdAsync(
            eventId,
            cancellationToken);

        return archivedEvent is null
            ? Results.NotFound()
            : Results.Ok(archivedEvent);
    }
}