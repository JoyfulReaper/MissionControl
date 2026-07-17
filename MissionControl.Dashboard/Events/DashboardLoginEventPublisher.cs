using JoyfulReaperLib.MissionControl;
using MissionControl.Dashboard.Authentication;

namespace MissionControl.Dashboard.Events;

public sealed class DashboardLoginEventPublisher(
    IMissionControlClient missionControlClient,
    ILogger<DashboardLoginEventPublisher> logger)
{
    private const string EventType =
        "missioncontrol.dashboard.user.login.succeeded";

    public async Task TryPublishAsync(
        DashboardUser user,
        string? remoteIpAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        DateTimeOffset authenticatedAtUtc =
            DateTimeOffset.UtcNow;

        var payload =
            new DashboardLoginSucceededEvent(
                UserId: user.Id,
                Username: user.Username,
                DisplayName: user.DisplayName,
                AuthenticatedAtUtc:
                    authenticatedAtUtc,
                Remote:
                    remoteIpAddress);

        try
        {
            bool published =
                await missionControlClient.TryPublishAsync(
                    eventType: EventType,
                    payload: payload,
                    occurredAt: authenticatedAtUtc,
                    correlationId: null,
                    cancellationToken:
                        cancellationToken);

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