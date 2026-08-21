using System.Text.Json;
using System.Text.Json.Serialization;

namespace MissionControl.Dashboard.Events;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DashboardLoginSucceededEvent))]
[JsonSerializable(typeof(DashboardLoginFailedEvent))]
internal sealed partial class DashboardEventJsonContext
    : JsonSerializerContext;