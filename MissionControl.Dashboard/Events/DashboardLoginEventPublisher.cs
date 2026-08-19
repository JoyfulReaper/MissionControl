using JoyfulReaperLib.MissionControl;
using MissionControl.Dashboard.Authentication;

namespace MissionControl.Dashboard.Events;

public sealed class DashboardLoginEventPublisher(
    IMissionControlClient missionControlClient,
    ILogger<DashboardLoginEventPublisher> logger)
{
    public async Task TryPublishAsync(
        DashboardUser user,
        string? remoteIpAddress,
        DashBoardEventTypes eventType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;

        try
        {
            bool published;

            switch (eventType)
            {
                case DashBoardEventTypes.LoginSucceeded:
                    {
                        var payload =
                            new DashboardLoginSucceededEvent(
                                UserId: user.Id,
                                Username: user.Username,
                                DisplayName: user.DisplayName,
                                AuthenticatedAtUtc: occurredAt,
                                Remote: remoteIpAddress);

                        published =
                            await missionControlClient.TryPublishAsync(
                                eventType:
                                    DashboardLoginSucceededEvent.EventType,
                                payload: payload,
                                payloadTypeInfo:
                                    DashboardEventJsonContext
                                        .Default
                                        .DashboardLoginSucceededEvent,
                                occurredAt: occurredAt,
                                correlationId: null,
                                cancellationToken: cancellationToken);

                        break;
                    }

                case DashBoardEventTypes.LoginFailed:
                    {
                        var payload =
                            new DashboardLoginFailedEvent(
                                Username: user.Username,
                                FailedAtUtc: occurredAt,
                                Remote: remoteIpAddress);

                        published =
                            await missionControlClient.TryPublishAsync(
                                eventType:
                                    DashboardLoginFailedEvent.EventType,
                                payload: payload,
                                payloadTypeInfo:
                                    DashboardEventJsonContext
                                        .Default
                                        .DashboardLoginFailedEvent,
                                occurredAt: occurredAt,
                                correlationId: null,
                                cancellationToken: cancellationToken);

                        break;
                    }

                default:
                    logger.LogWarning(
                        "Unknown dashboard login event type {EventType} for user {UserId}.",
                        eventType,
                        user.Id);

                    return;
            }

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control rejected or failed to publish dashboard login event {EventType} for user {UserId}.",
                    eventType,
                    user.Id);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Publishing dashboard login event {EventType} was canceled for user {UserId}.",
                eventType,
                user.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish dashboard login event {EventType} for user {UserId}.",
                eventType,
                user.Id);
        }
    }
}