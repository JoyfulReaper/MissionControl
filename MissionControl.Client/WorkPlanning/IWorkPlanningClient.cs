namespace MissionControl.Client.WorkPlanning;

public interface IWorkPlanningClient
{
    Task<DailyWorkPick?> GetDailyPickAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkPlanningWorkItem>>
        GetWorkItemsAsync(CancellationToken cancellationToken = default);

    Task<WorkPlanningTodo> CreateTodoAsync(
        int workItemId,
        CreateWorkPlanningTodoRequest request,
        CancellationToken cancellationToken = default);
}