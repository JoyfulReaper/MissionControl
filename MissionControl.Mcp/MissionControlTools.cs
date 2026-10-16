using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace MissionControl.Mcp;

[McpServerToolType]
public static class MissionControlTools
{
    [McpServerTool]
    [Description(
        "Returns the most recent archived Mission Control event envelopes, " +
        "including their payloads. Use this to inspect recent system activity.")]
    public static Task<JsonElement> GetRecentEventsAsync(
        ArchiveClient archiveClient,
        [Description(
            "Maximum number of recent events to return. " +
            "Defaults to 50 and is limited to 100.")]
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return archiveClient.GetRecentEventsAsync(
            limit,
            cancellationToken);
    }

    [McpServerTool]
    [Description(
        "Returns lightweight summaries of the most recent archived " +
        "Mission Control events without their payloads. Use this first " +
        "when surveying recent activity.")]
    public static Task<JsonElement> GetRecentEventSummariesAsync(
        ArchiveClient archiveClient,
        [Description(
            "Maximum number of recent event summaries to return. " +
            "Defaults to 50 and is limited to 100.")]
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return archiveClient.GetRecentEventSummariesAsync(
            limit,
            cancellationToken);
    }

    [McpServerTool]
    [Description(
        "Returns one archived Mission Control event by its event ID, " +
        "including the full payload. Use this after identifying an " +
        "interesting event from the event summary tool.")]
    public static Task<JsonElement?> GetEventByIdAsync(
    ArchiveClient archiveClient,
    [Description(
        "The Mission Control event ID to retrieve.")]
    Guid eventId,
    CancellationToken cancellationToken = default)
    {
        return archiveClient.GetEventByIdAsync(
            eventId,
            cancellationToken);
    }

    [McpServerTool]
    [Description(
        "Returns aggregate Mission Control Archive statistics for a recent time window, " +
        "including the busiest event types and sources. Use this before loading " +
        "individual events when you want an overview of recent activity.")]
    public static Task<JsonElement> GetEventStatisticsAsync(
        ArchiveClient archiveClient,
        [Description(
        "Number of recent hours to include. Defaults to 24 and is limited to 1-168.")]
    int hours = 24,
        [Description(
        "Maximum number of top event types and sources to return. " +
        "Defaults to 5 and is limited to 1-20.")]
    int top = 5,
        CancellationToken cancellationToken = default)
    {
        return archiveClient.GetEventStatisticsAsync(
            hours,
            top,
            cancellationToken);
    }
}