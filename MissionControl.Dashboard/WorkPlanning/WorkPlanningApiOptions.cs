namespace MissionControl.Dashboard.WorkPlanning;

public sealed class WorkPlanningApiOptions
{
    public const string SectionName = "WorkPlanningApi";

    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;
}