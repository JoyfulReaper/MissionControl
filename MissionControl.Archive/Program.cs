using JoyfulReaperLib.Sqlite;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MissionControl.Archive.Health;
using MissionControl.Archive.Processing;
using MissionControl.Archive.Processing.RabbitMq;
using MissionControl.Archive.Storage;
using MissionControl.Archive.Storage.Sqlite;

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

builder.Services.AddHostedService<RabbitMqEventConsumer>();

builder.Services
    .AddHealthChecks()
    .AddCheck<SqliteArchiveHealthCheck>(
        "sqlite",
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