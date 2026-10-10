namespace MissionControl.Client.WorkPlanning;

public sealed record WorkPlanningAvailability(bool Enabled);

public interface IWorkPlanningAvailabilityClient
{
    Task<WorkPlanningAvailability> GetAsync(CancellationToken cancellationToken = default);
}
