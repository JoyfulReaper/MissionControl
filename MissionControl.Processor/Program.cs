using JoyfulReaperLib.Sqlite;
using MissionControl.Processor.Processing;
using MissionControl.Processor.Processing.RabbitMq;
using MissionControl.Processor.Storage;
using MissionControl.Processor.Storage.Sqlite;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddHostedService<RabbitMqEventConsumer>();

var host = builder.Build();
host.Run();
