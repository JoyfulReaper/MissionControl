namespace MissionControl.Dashboard.GreenCloud;

public sealed class GreenCloudOptions
{
    public const string SectionName = "GreenCloud";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://cp.green.cloud/";

    // Temporary backwards-compatible single-server setting.
    // Mobile /api/mobile/bandwidth can continue using this.
    public string ServerId { get; set; } = string.Empty;

    public List<GreenCloudServerOptions> Servers { get; set; } = [];

    public string ApiToken { get; set; } = string.Empty;

    public int PollSeconds { get; set; } = 300;
}

public sealed class GreenCloudServerOptions
{
    public string NodeId { get; set; } = string.Empty;

    public string ServerId { get; set; } = string.Empty;
}