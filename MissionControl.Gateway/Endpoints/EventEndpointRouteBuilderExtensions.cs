/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using MissionControl.Contracts;
using MissionControl.Gateway.Messaging;
using MissionControl.Gateway.Security;
using System.Text.Json;

namespace MissionControl.Gateway.Endpoints;

public static class EventEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapEventPublishingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        return endpoints
            .MapPost("/api/events", HandlePublishEventAsync)
            .WithName("PublishEvent")
            .WithTags("Events")
            .Produces<PublishEventAcceptedResponse>(
                StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> HandlePublishEventAsync(
        PublishEventRequest request,
        HttpRequest httpRequest,
        IEventSourceResolver sourceResolver,
        IEventPublisher publisher,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue(
                EventSourceOptions.ApiKeyHeaderName,
                out var apiKeyValues) ||
            apiKeyValues.Count != 1 ||
            !sourceResolver.TryResolve(
                apiKeyValues[0],
                out var source))
        {
            return Results.Unauthorized();
        }

        var errors = new Dictionary<string, string[]>();

        if (request.EventId == Guid.Empty)
        {
            errors[nameof(request.EventId)] = ["EventId must not be empty"];
        }

        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            errors[nameof(request.EventType)] =
                ["EventType is required."];
        }

        if (request.SchemaVersion < 1)
        {
            errors[nameof(request.SchemaVersion)] =
                ["SchemaVersion must be at least 1."];
        }

        if (request.OccurredAt == default)
        {
            errors[nameof(request.OccurredAt)] =
                ["OccurredAt is required."];
        }

        if (request.Payload.ValueKind != JsonValueKind.Object)
        {
            errors[nameof(request.Payload)] =
                ["Payload must be a JSON object."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var envelope = new IntegrationEventEnvelope(
            request.EventId,
            request.EventType,
            Source: source,
            request.SchemaVersion,
            request.OccurredAt,
            ReceivedAt: DateTimeOffset.UtcNow,
            request.CorrelationId,
            null,
            request.Payload);

        try
        {
            await publisher.PublishAsync(
                envelope,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Results.StatusCode(
                StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted(
            value: new PublishEventAcceptedResponse(request.EventId));
    }
}
