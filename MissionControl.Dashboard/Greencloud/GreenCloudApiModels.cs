using System.Text.Json.Serialization;

namespace MissionControl.Dashboard.GreenCloud;

internal sealed class GreenCloudServerResponse
{
    [JsonPropertyName("data")]
    public GreenCloudServer Data { get; init; } = new();
}

internal sealed class GreenCloudServer
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public GreenCloudServerState State { get; init; } = new();

    [JsonPropertyName("network")]
    public GreenCloudNetwork Network { get; init; } = new();

    [JsonPropertyName("currentMonthlyPeriod")]
    public GreenCloudMonthlyPeriod CurrentMonthlyPeriod { get; init; } = new();
}

internal sealed class GreenCloudServerState
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("network")]
    public GreenCloudStateNetwork Network { get; init; } = new();
}

internal sealed class GreenCloudStateNetwork
{
    [JsonPropertyName("primary")]
    public GreenCloudStatePrimary Primary { get; init; } = new();
}

internal sealed class GreenCloudStatePrimary
{
    [JsonPropertyName("traffic")]
    public GreenCloudTraffic Traffic { get; init; } = new();
}

internal sealed class GreenCloudTraffic
{
    [JsonPropertyName("rx")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double Rx { get; init; }

    [JsonPropertyName("tx")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double Tx { get; init; }

    [JsonPropertyName("total")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double Total { get; init; }
}

internal sealed class GreenCloudNetwork
{
    [JsonPropertyName("primary")]
    public GreenCloudPrimaryNetwork Primary { get; init; } = new();
}

internal sealed class GreenCloudPrimaryNetwork
{
    [JsonPropertyName("limit")]
    public string Limit { get; init; } = string.Empty;
}

internal sealed class GreenCloudMonthlyPeriod
{
    [JsonPropertyName("start")]
    public DateTimeOffset Start { get; init; }

    [JsonPropertyName("end")]
    public DateTimeOffset End { get; init; }
}