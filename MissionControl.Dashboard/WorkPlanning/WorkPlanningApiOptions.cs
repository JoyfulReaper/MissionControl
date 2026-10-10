namespace MissionControl.Dashboard.WorkPlanning;

public sealed class WorkPlanningApiOptions
{
    public const string SectionName = "WorkPlanningApi";
    public bool Enabled { get; init; } = true;

    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;
}
