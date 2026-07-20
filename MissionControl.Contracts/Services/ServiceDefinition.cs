namespace MissionControl.Contracts.Services;

public sealed class ServiceDefinition
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string Group { get; set; }

    public required string Summary { get; set; }

    public required string Description { get; set; }

    public string? ContainerName { get; set; }

    public required string Visibility { get; set; }

    public string? Protocol { get; set; }

    public string? ProtocolServiceKey { get; set; }

    public string? Endpoint { get; set; }

    public string? Image { get; set; }

    public string? ApplicationUrl { get; set; }

    public string? SourceUrl { get; set; }

    public List<string> SearchTerms { get; set; } = [];
}