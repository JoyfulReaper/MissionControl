namespace MissionControl.Dashboard.GreenCloud;

public sealed class GreenCloudOptions
{
    public const string SectionName = "GreenCloud";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://cp.green.cloud/";
    public string ServerId { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public int PollSeconds { get; set; } = 300;
}