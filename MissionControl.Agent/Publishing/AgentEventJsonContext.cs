using MissionControl.Agent.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MissionControl.Agent.Publishing;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(NodeSnapshotEvent))]
internal sealed partial class AgentEventJsonContext : JsonSerializerContext;