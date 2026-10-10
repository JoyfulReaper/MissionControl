using MissionControl.Client.WorkPlanning;

namespace MissionControl.Dashboard.WorkPlanning;

internal sealed class DisabledWorkPlanningClient : IWorkPlanningClient
{
    private static Task<T> Disabled<T>(CancellationToken token) => token.IsCancellationRequested
        ? Task.FromCanceled<T>(token)
        : Task.FromException<T>(new InvalidOperationException("Work Planning is disabled."));

    public Task<DailyWorkPick?> GetDailyPickAsync(CancellationToken cancellationToken = default) => Disabled<DailyWorkPick?>(cancellationToken);
    public Task<RandomWorkPick?> GetRandomPickAsync(bool favorPriority = false, CancellationToken cancellationToken = default) => Disabled<RandomWorkPick?>(cancellationToken);
    public Task<IReadOnlyList<WorkPlanningWorkItem>> GetWorkItemsAsync(CancellationToken cancellationToken = default) => Disabled<IReadOnlyList<WorkPlanningWorkItem>>(cancellationToken);
    public Task<WorkPlanningTodo> CreateTodoAsync(int workItemId, CreateWorkPlanningTodoRequest request, CancellationToken cancellationToken = default) => Disabled<WorkPlanningTodo>(cancellationToken);
}
