namespace MissionControl.Dashboard.Servitro;

public sealed class ServitroOptions
{
    public const string SectionName = "Servitro";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://platform.servitro.com/api/";
    public string Token { get; set; } = string.Empty;
    public int PollSeconds { get; set; } = 300;
    public List<ServitroServerOptions> Servers { get; set; } = [];
}

public sealed class ServitroServerOptions
{
    public string NodeId { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
}
