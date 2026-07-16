/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

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

        return endpoints;
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
        DateTimeOffset? before,
        IIntegrationEventQuery query,
        CancellationToken cancellationToken)
    {
        int effectiveLimit =
            Math.Clamp(limit ?? 50, 1, 200);

        var events = await query.GetRecentSummariesAsync(
            effectiveLimit,
            source,
            eventType,
            before,
            cancellationToken);

        return Results.Ok(events);
    }
}