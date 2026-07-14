/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using MissionControl.Archive.Storage;

namespace MissionControl.Archive.Endpoints;

public static class ArchiveEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapArchiveEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        return endpoints
            .MapGet("/api/events", HandleGetRecentEventsAsync)
            .WithName("GetRecentEvents")
            .WithTags("Events");
    }

    private static async Task<IResult> HandleGetRecentEventsAsync(
        int? limit,
        string? source,
        string? eventType,
        DateTimeOffset? before,
        IIntegrationEventQuery query,
        CancellationToken cancellationToken)
    {
        var effectiveLimit = Math.Clamp(limit ?? 50, 1, 200);
        var events = await query.GetRecentAsync(
            effectiveLimit,
            source,
            eventType,
            before,
            cancellationToken);

        return Results.Ok(events);
    }
}
