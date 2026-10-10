using System.Net.Http.Json;

namespace MissionControl.Client.WorkPlanning;

public sealed class WorkPlanningAvailabilityClient(HttpClient client) : IWorkPlanningAvailabilityClient
{
    public async Task<WorkPlanningAvailability> GetAsync(CancellationToken cancellationToken = default) =>
        await client.GetFromJsonAsync<WorkPlanningAvailability>("api/mobile/work-planning/status", cancellationToken)
        ?? throw new InvalidOperationException("Work availability could not be retrieved.");
}
