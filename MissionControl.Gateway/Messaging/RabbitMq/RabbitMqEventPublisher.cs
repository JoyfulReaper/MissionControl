/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

// TODO: Should we move some of these into to JoyfulReaperLibrary?

using Microsoft.Extensions.Options;
using MissionControl.Contracts;
using MissionControl.Observability.RabbitMq;
using RabbitMQ.Client;
using System.Text.Json;

namespace MissionControl.Gateway.Messaging.RabbitMq;

public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable, IRabbitMqConnectionStatus
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    public RabbitMqEventPublisher(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
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

    public async Task PublishAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent);
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            Persistent = true,
            MessageId = integrationEvent.EventId.ToString(),
            Type = integrationEvent.EventType,
            CorrelationId = integrationEvent.CorrelationId,
            AppId = integrationEvent.Source
        };

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureConnectedAsync(cancellationToken);

            await _channel!.BasicPublishAsync(
                exchange: RabbitMqTopology.EventsExchange,
                routingKey: integrationEvent.EventType,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken
            );

            _logger.LogDebug(
                "Published event {EventType} from {Source} with ID {EventId}",
                integrationEvent.EventType,
                integrationEvent.Source,
                integrationEvent.EventId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await EnsureConnectedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
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
            var channelOption = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            );

            var channel = await connection.CreateChannelAsync(channelOption, cancellationToken);
            await channel.ExchangeDeclareAsync(
                exchange: RabbitMqTopology.EventsExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

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
}