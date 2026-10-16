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
}