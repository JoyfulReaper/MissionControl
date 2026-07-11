using MissionControl.Processor.Processing.RabbitMq;
using MissionControl.Processor.Processing;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddSingleton<
    IIntegrationEventProcessor,
    LoggingIntegrationEventProcessor>();

builder.Services.AddHostedService<RabbitMqEventConsumer>();

var host = builder.Build();
host.Run();
