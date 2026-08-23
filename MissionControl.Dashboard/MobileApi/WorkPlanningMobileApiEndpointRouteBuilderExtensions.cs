using MissionControl.Client.WorkPlanning;

namespace MissionControl.Dashboard.MobileApi;

public static class
    WorkPlanningMobileApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder
        MapWorkPlanningMobileApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api =
            endpoints
                .MapGroup(
                    "/api/mobile/work-planning")
                .WithTags(
                    "Mobile API",
                    "Work Planning")
                .RequireAuthorization(MobileApiAuthenticationDefaults.Policy);

        api.MapGet(
                "/daily-pick",
                GetDailyPickAsync)
            .WithName("GetMobileWorkPlanningDailyPick");

        api.MapGet(
            "/random-pick",
            GetRandomPickAsync)
        .WithName("GetMobileWorkPlanningRandomPick");

        api.MapGet(
                "/work-items",
                GetWorkItemsAsync)
            .WithName("GetMobileWorkPlanningWorkItems");

        api.MapPost(
                "/work-items/{workItemId:int}/todos",
                CreateTodoAsync)
            .WithName("CreateMobileWorkPlanningTodo");

        return endpoints;
    }

    private static async Task<IResult>
        GetDailyPickAsync(
            IWorkPlanningClient workPlanningClient,
            HttpResponse response,
            CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            DailyWorkPick? pick =
                await workPlanningClient
                    .GetDailyPickAsync(cancellationToken);

            return pick is null
                ? Results.NoContent()
                : Results.Ok(pick);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedException(exception))
        {
            return CreateUnavailableResult(exception);
        }
    }

    private static async Task<IResult>
    GetRandomPickAsync(
        bool favorPriority,
        IWorkPlanningClient workPlanningClient,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            RandomWorkPick? pick = await workPlanningClient
                .GetRandomPickAsync(
                    favorPriority,
                    cancellationToken);

            return pick is null
                ? Results.NoContent()
                : Results.Ok(pick);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedException(exception))
        {
            return CreateUnavailableResult(exception);
        }
    }

    private static async Task<IResult>
        GetWorkItemsAsync(
            IWorkPlanningClient workPlanningClient,
            HttpResponse response,
            CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            IReadOnlyList<WorkPlanningWorkItem>
                workItems = await workPlanningClient
                    .GetWorkItemsAsync(cancellationToken);

            return Results.Ok(workItems);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedException(exception))
        {
            return CreateUnavailableResult(exception);
        }
    }

    private static async Task<IResult>
        CreateTodoAsync(
            int workItemId,
            CreateWorkPlanningTodoRequest request,
            IWorkPlanningClient workPlanningClient,
            HttpResponse response,
            CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            WorkPlanningTodo todo =
                await workPlanningClient
                    .CreateTodoAsync(
                        workItemId,
                        request,
                        cancellationToken);

            return Results.Ok(todo);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsExpectedException(exception))
        {
            return CreateUnavailableResult(exception);
        }
    }

    private static bool IsExpectedException(
        Exception exception)
    {
        return exception is
            HttpRequestException or
            TaskCanceledException or
            InvalidOperationException;
    }

    private static IResult CreateUnavailableResult(
        Exception exception)
    {
        string detail =
            exception switch
            {
                TaskCanceledException =>
                    "The Work Planning request timed out.",

                HttpRequestException =>
                    "The Work Planning service could not be reached.",

                _ =>
                    "The Work Planning request failed."
            };

        return Results.Problem(
            title: "Work Planning unavailable.",
            detail: detail,
            statusCode: StatusCodes.Status502BadGateway);
    }

    private static void DisableResponseCaching(
        HttpResponse response)
    {
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Pragma"] = "no-cache";
    }
}