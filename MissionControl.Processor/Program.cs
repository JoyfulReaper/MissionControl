using MissionControl.Processor.Processing.RabbitMq;
using MissionControl.Processor.Processing;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(
        RabbitMqOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<
    IIntegrationEventProcessor,
    LoggingIntegrationEventProcessor>();

builder.Services.AddHostedService<RabbitMqEventConsumer>();

var host = builder.Build();
host.Run();
