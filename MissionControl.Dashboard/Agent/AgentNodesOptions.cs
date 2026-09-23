namespace MissionControl.Dashboard.Agents;

public sealed class AgentNodesOptions
{
    public const string SectionName = "Agents";

    public List<AgentNodeOptions> Nodes { get; set; } = [];
}

public sealed class AgentNodeOptions
{
    public string NodeId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;
}