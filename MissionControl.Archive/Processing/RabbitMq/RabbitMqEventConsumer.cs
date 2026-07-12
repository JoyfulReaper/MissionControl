using Microsoft.Extensions.Options;
using MissionControl.Contracts;
using MissionControl.Observability.RabbitMq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;

namespace MissionControl.Archive.Processing.RabbitMq;

public sealed class RabbitMqEventConsumer : BackgroundService, IAsyncDisposable, IRabbitMqConnectionStatus
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventConsumer> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IIntegrationEventProcessor _processor;
    private string? _consumerTag;

    public RabbitMqEventConsumer(
            IOptions<RabbitMqOptions> options,
            IIntegrationEventProcessor processor,
            ILogger<RabbitMqEventConsumer> logger)
    {
        _options = options.Value;
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureConnectedAsync(stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(_channel!);

        consumer.ReceivedAsync += (_, delivery) =>
            HandleMessageAsync(delivery, stoppingToken);

        _consumerTag = await _channel!.BasicConsumeAsync(
            queue: RabbitMqTopology.ArchiveQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken
        );

        _logger.LogInformation(
            "Consuming events from queue {Queue}",
            RabbitMqTopology.ArchiveQueue);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal Application shutdown
        }
    }

    private async Task HandleMessageAsync(
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken)
    {
        var body = delivery.Body.ToArray();
        try
        {
            var integrationEvent =
            JsonSerializer.Deserialize<IntegrationEventEnvelope>(
                body,
                JsonOptions)
            ?? throw new JsonException(
                "Message deserialized to null.");

            await _processor.ProcessAsync(integrationEvent, cancellationToken);
            await _channel!.BasicAckAsync(
                deliveryTag: delivery.DeliveryTag,
                multiple: false,
                cancellationToken: cancellationToken
            );

            _logger.LogDebug(
                "Acknowledged event {EventId}",
                integrationEvent.EventId);
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "Rejected malformed RabbitMQ message with delivery tag {DeliveryTag}",
                delivery.DeliveryTag);

            await _channel!.BasicRejectAsync(
                deliveryTag: delivery.DeliveryTag,
                requeue: false,
                cancellationToken: CancellationToken.None);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                exception,
                "Failed to process RabbitMQ message with delivery tag {DeliveryTag}",
                delivery.DeliveryTag);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to process RabbitMQ message with delivery tag {DeliveryTag}",
                delivery.DeliveryTag);

            // Temporary retry policy:
            // retry once, then discard until we add a dead-letter queue.
            await _channel!.BasicNackAsync(
                deliveryTag: delivery.DeliveryTag,
                multiple: false,
                requeue: !delivery.Redelivered,
                cancellationToken: CancellationToken.None);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
                "Stopping RabbitMQ event consumer");

        if (_channel?.IsOpen == true &&
            !string.IsNullOrWhiteSpace(_consumerTag))
        {
            try
            {
                await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error cancelling RabbitMQ consumer");
            }
        }

        await DisposeRabbitMqResourcesAsync();
        await base.StopAsync(cancellationToken);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection?.IsOpen == true && _channel?.IsOpen == true)
            return;

        await DisposeRabbitMqResourcesAsync();

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = _options.ClientProvidedName,

            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        _logger.LogInformation(
            "Connecting to RabbitMQ at {HostName}:{Port}, vhost {VirtualHost}",
            _options.HostName,
            _options.Port,
            _options.VirtualHost);

        var connection = await factory.CreateConnectionAsync(cancellationToken);

        try
        {
            var channel = await connection.CreateChannelAsync(
                cancellationToken: cancellationToken);

            await channel.ExchangeDeclareAsync(
                exchange: RabbitMqTopology.EventsExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                queue: RabbitMqTopology.ArchiveQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                queue: RabbitMqTopology.ArchiveQueue,
                exchange: RabbitMqTopology.EventsExchange,
                routingKey: RabbitMqTopology.AllEventsRoutingKey,
                arguments: null,
                cancellationToken: cancellationToken);

            await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: 10,
                global: false,
                cancellationToken: cancellationToken
            );

            _connection = connection;
            _channel = channel;

            _logger.LogInformation(
                "Connected to RabbitMQ and declared exchange {Exchange}",
                RabbitMqTopology.EventsExchange);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public RabbitMqConnectionSnapshot GetSnapshot()
    {
        var connection = _connection;
        var channel = _channel;

        return new RabbitMqConnectionSnapshot(
            ConnectionOpen: connection?.IsOpen == true,
            ChannelOpen: channel?.IsOpen == true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync();

        try
        {
            if (_disposed)
                return;

            _disposed = true;
            await DisposeRabbitMqResourcesAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeRabbitMqResourcesAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Error while disposing RabbitMQ channel");
            }
            _channel = null;
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disposing RabbitMQ connection");
            }
            _connection = null;
        }
    }
}