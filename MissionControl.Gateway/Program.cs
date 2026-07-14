/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

// TODO: Clean up this file

using Kgivler.Api.GitActivity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MissionControl.Contracts;
using MissionControl.Gateway.Integrations.GitHub;
using MissionControl.Gateway.Messaging;
using MissionControl.Gateway.Messaging.RabbitMq;
using MissionControl.Gateway.Security;
using MissionControl.Observability.RabbitMq;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(
        builder.Configuration.GetSection(
            RabbitMqOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.HostName),
        "RabbitMQ hostname is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.UserName),
        "RabbitMQ username is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Password),
        "RabbitMQ password is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.VirtualHost),
        "RabbitMQ virtual host is required.")
    .ValidateOnStart();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Mission Control Gateway";
});

builder.Services.AddHealthChecks();

builder.Services.AddSingleton<RabbitMqEventPublisher>();

builder.Services.AddSingleton<IEventPublisher>(
    services =>
        services.GetRequiredService<RabbitMqEventPublisher>());

builder.Services.AddSingleton<IRabbitMqConnectionStatus>(
    services =>
        services.GetRequiredService<RabbitMqEventPublisher>());

builder.Services.AddHostedService<
    RabbitMqPublisherConnectionWorker>();

builder.Services
    .AddOptions<EventSourceOptions>()
    .Bind(
        builder.Configuration.GetSection(
            EventSourceOptions.SectionName))
    .Validate(
        options => options.Sources.Count > 0,
        "At least one event source must be configured.")
    .Validate(
        options => options.Sources.All(source =>
            !string.IsNullOrWhiteSpace(source.Name)),
        "Every event source must have a name.")
    .Validate(
        options => options.Sources.All(source =>
            !string.IsNullOrWhiteSpace(source.ApiKey)),
        "Every event source must have an API key.")
    .Validate(
        options => options.Sources.All(source =>
            source.ApiKey.Length >= 32),
        "Every event source API key must contain at least 32 characters.")
    .Validate(
        options =>
            options.Sources
                .Select(source => source.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == options.Sources.Count,
        "Event source names must be unique.")
    .Validate(
        options =>
            options.Sources
                .Select(source => source.ApiKey)
                .Distinct(StringComparer.Ordinal)
                .Count() == options.Sources.Count,
        "Event source API keys must be unique.")
    .ValidateOnStart();

builder.Services
    .AddOptions<GitHubWebhookOptions>()
    .Bind(
        builder.Configuration.GetSection(
            GitHubWebhookOptions.SectionName))
    .Validate(
        options =>
            !options.Enabled ||
            !string.IsNullOrWhiteSpace(options.Secret),
        "GitHubWebhook:Secret is required when the webhook is enabled.")
    .Validate(
        options =>
            !options.Enabled ||
            options.Secret.Length >= 32,
        "GitHubWebhook:Secret must contain at least 32 characters when the webhook is enabled.")
    .Validate(
        options =>
            !options.Enabled ||
            !string.IsNullOrWhiteSpace(options.AllowedOwner),
        "GitHubWebhook:AllowedOwner is required when the webhook is enabled.")
    .Validate(
        options =>
            options.MaxPayloadBytes is > 0 and <= 25 * 1024 * 1024,
        "GitHubWebhook:MaxPayloadBytes must be between 1 byte and 25 MB.")
    .ValidateOnStart();

builder.Services.AddSingleton<GitHubWebhookSignatureValidator>();
builder.Services.AddSingleton<IEventSourceResolver, ApiKeyEventSourceResolver>();

builder.Services
    .AddOptions<GitActivityOptions>()
    .BindConfiguration(GitActivityOptions.SectionName)
    .Validate(
        options =>
            Uri.TryCreate(
                options.ArchiveBaseUrl,
                UriKind.Absolute,
                out _),
        "GitActivity:ArchiveBaseUrl must be an absolute URL.")
    .Validate(
        options => options.CacheSeconds > 0,
        "GitActivity:CacheSeconds must be greater than zero.")
    .Validate(
        options => options.ArchiveQueryLimit is > 0 and <= 100,
        "GitActivity:ArchiveQueryLimit must be between 1 and 100.")
    .Validate(
        options => options.PublicResultLimit is > 0 and <= 50,
        "GitActivity:PublicResultLimit must be between 1 and 50.")
    .Validate(
        options => options.AllowedRepositories.Length > 0,
        "GitActivity:AllowedRepositories must contain at least one repository.")
    .Validate(
        options => options.AllowedBranches.Length > 0,
        "GitActivity:AllowedBranches must contain at least one branch.")
    .ValidateOnStart();

builder.Services.AddHttpClient<ArchiveClient>(
    (services, client) =>
    {
        var options = services
            .GetRequiredService<IOptions<GitActivityOptions>>()
            .Value;

        client.BaseAddress = new Uri(options.ArchiveBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(5);
    });

builder.Services
    .AddHealthChecks()
    .AddCheck<RabbitMqConnectionHealthCheck>(
        "rabbitmq",
        tags: ["ready"]);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGitHubWebhook();

app.MapPost("/api/events", async (
    PublishEventRequest request,
    HttpRequest httpRequest,
    IEventSourceResolver sourceResolver,
    IEventPublisher publisher,
    CancellationToken cancellation) =>
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
            await publisher.PublishAsync(envelope, cancellationToken: cancellation);
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Results.StatusCode(
                StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted(value: new PublishEventAcceptedResponse(request.EventId));
    }
)
.WithName("PublishEvent")
.WithTags("Events")
    .Produces<PublishEventAcceptedResponse>(
        StatusCodes.Status202Accepted)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/github/activity/debug",
    async (
        ArchiveClient archive,
        CancellationToken cancellationToken) =>
    {
        var events = await archive.GetRecentGitPushesAsync(
            cancellationToken: cancellationToken);

        return Results.Ok(events);
    });

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = _ => false
    });

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration =>
            registration.Tags.Contains("ready")
    });

app.Run();