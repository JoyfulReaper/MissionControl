using System.Net;
using System.Net.Http.Json;

namespace MissionControl.Client.WorkPlanning;

public sealed class WorkPlanningClient(
    HttpClient client)
    : IWorkPlanningClient
{
    public async Task<DailyWorkPick?> GetDailyPickAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.GetAsync("api/daily-pick", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await ReadRequiredAsync<DailyWorkPick>(
            response,
            cancellationToken);
    }

    public async Task<IReadOnlyList<WorkPlanningWorkItem>>
        GetWorkItemsAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.GetAsync("api/work-items", cancellationToken);

        response.EnsureSuccessStatusCode();

        var workItems =
            await ReadRequiredAsync<
                WorkPlanningWorkItem[]>(
                response,
                cancellationToken);

        return workItems;
    }

    public async Task<WorkPlanningTodo> CreateTodoAsync(
        int workItemId,
        CreateWorkPlanningTodoRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.PostAsJsonAsync(
                $"api/work-items/{workItemId}/todos",
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        return await ReadRequiredAsync<WorkPlanningTodo>(
            response,
            cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

        return value ??
            throw new InvalidOperationException("The Work Planning API response was empty.");
    }
}