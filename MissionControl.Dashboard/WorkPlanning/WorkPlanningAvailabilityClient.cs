using Microsoft.Extensions.Options;
using MissionControl.Client.WorkPlanning;

namespace MissionControl.Dashboard.WorkPlanning;

internal sealed class DashboardWorkPlanningAvailabilityClient(IOptions<WorkPlanningApiOptions> options)
    : IWorkPlanningAvailabilityClient
{
    public Task<WorkPlanningAvailability> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new WorkPlanningAvailability(options.Value.Enabled));
    }
}
