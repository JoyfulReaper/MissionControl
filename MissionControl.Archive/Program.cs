using JoyfulReaperLib.Sqlite;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MissionControl.Archive.Health;
using MissionControl.Archive.Processing;
using MissionControl.Archive.Processing.RabbitMq;
using MissionControl.Archive.Storage;
using MissionControl.Archive.Storage.Sqlite;
using MissionControl.Messaging.RabbitMq;
using MissionControl.Observability.RabbitMq;

var builder = WebApplication.CreateBuilder(args);

var archiveOptions =
    builder.Configuration
        .GetSection(SqliteEventArchiveOptions.SectionName)
        .Get<SqliteEventArchiveOptions>()
        ?? new SqliteEventArchiveOptions();

var archiveConnectionString = SqliteDatabaseInitializer.Initialize(
    archiveOptions.DatabaseFileName,
    SqliteEventArchiveSchema.Sql,
    archiveOptions.BasePath);

builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(
        RabbitMqOptions.SectionName))
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

builder.Services
    .AddOptions<RabbitMqConsumerOptions>()
    .BindConfiguration(RabbitMqConsumerOptions.SectionName)
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.ExchangeName),
        "RabbitMqConsumer:ExchangeName is required.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.QueueName),
        "RabbitMqConsumer:QueueName is required.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.RoutingKey),
        "RabbitMqConsumer:RoutingKey is required.")
    .Validate(
        options => options.PrefetchCount > 0,
        "RabbitMqConsumer:PrefetchCount must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Mission Control Archive";
});

builder.Services.AddSingleton(
    new SqliteEventArchiveConnection(
        archiveConnectionString));

builder.Services.AddSingleton<
    IIntegrationEventArchive,
    SqliteEventArchive>();

builder.Services.AddSingleton<
    IIntegrationEventProcessor,
    ArchivingIntegrationEventProcessor>();

builder.Services.AddSingleton<IIntegrationEventQuery, SqliteEventQuery>();

builder.Services.AddSingleton<RabbitMqEventConsumer>();

builder.Services.AddSingleton<IRabbitMqConnectionStatus>(
    services =>
        services.GetRequiredService<RabbitMqEventConsumer>());

builder.Services.AddHostedService(
    services =>
        services.GetRequiredService<RabbitMqEventConsumer>());

builder.Services
    .AddHealthChecks()
    .AddCheck<SqliteArchiveHealthCheck>(
        "sqlite",
        tags: ["ready"])
    .AddCheck<RabbitMqConnectionHealthCheck>(
        "rabbitmq",
        tags: ["ready"]);

var app = builder.Build();

app.MapGet("/api/events",
    async (int? limit,
    string? source,
    string? eventType,
    DateTimeOffset? before,
    IIntegrationEventQuery query,
    CancellationToken cancellationToken) =>
    {
        var effectiveLimit = Math.Clamp(limit ?? 50, 1, 200);
        var events = await query.GetRecentAsync(
            effectiveLimit,
            source,
            eventType,
            before,
            cancellationToken);

        return Results.Ok(events);
    }).WithName("GetRecentEvents")
    .WithTags("Events");

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