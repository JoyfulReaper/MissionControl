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

        DateTimeOffset occuredAt =
            DateTimeOffset.UtcNow;


        try
        {
            bool published = false;
            switch (eventType)
            {
                case DashBoardEventTypes.LoginSucceeded:
                    var payloadSucess =
                        new DashboardLoginSucceededEvent(
                            UserId: user.Id,
                            Username: user.Username,
                            DisplayName: user.DisplayName,
                            AuthenticatedAtUtc:
                                occuredAt,
                            Remote:
                                remoteIpAddress);

                    published =
                       await missionControlClient.TryPublishAsync(
                           eventType: DashboardLoginSucceededEvent.EventType,
                           payload: payloadSucess,
                           occurredAt: occuredAt,
                           correlationId: null,
                           cancellationToken:
                               cancellationToken);
                    break;
                case DashBoardEventTypes.LoginFailed:
                    var payloadFailure =
                        new DashboardLoginFailedEvent(
                            Username: user.Username,
                            FailedAtUtc:
                                occuredAt,
                            Remote:
                                remoteIpAddress);

                    published =
                       await missionControlClient.TryPublishAsync(
                           eventType: DashboardLoginFailedEvent.EventType,
                           payload: payloadFailure,
                           occurredAt: occuredAt,
                           correlationId: null,
                           cancellationToken:
                               cancellationToken);
                    break;
                default:
                    logger.LogWarning(
                        "Unknown dashboard login event type {EventType} for user {UserId}.",
                        eventType,
                        user.Id);
                    break;
            }

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control rejected or failed to publish the successful login event for dashboard user {UserId}.",
                    user.Id);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Publishing the successful login event was canceled for dashboard user {UserId}.",
                user.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish the successful login event for dashboard user {UserId}.",
                user.Id);
        }
    }
}