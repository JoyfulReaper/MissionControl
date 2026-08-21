namespace MissionControl.Client.WorkPlanning;

public sealed record WorkPlanningWorkItem(
    int Id,
    string Name,
    string? Description,
    string? Url,
    string Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastWorkedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ArchivedAt,
    int TodoCount,
    int ActiveTodoCount);

public sealed record WorkPlanningTodo(
    int Id,
    int WorkItemId,
    string WorkItemName,
    string Task,
    string Energy,
    string Effort,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record DailyWorkPick(
    DateOnly Date,
    WorkPlanningTodo Todo,
    DateTimeOffset? LastWorkedAt);

public sealed record CreateWorkPlanningTodoRequest(
    string Task,
    string? Energy = null,
    string? Effort = null);