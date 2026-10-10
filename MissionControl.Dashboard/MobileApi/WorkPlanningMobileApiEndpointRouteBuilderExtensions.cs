using MissionControl.Client.WorkPlanning;

using Microsoft.Extensions.Options;
using MissionControl.Dashboard.WorkPlanning;

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

        api.MapGet("/status", (IOptions<WorkPlanningApiOptions> options, HttpResponse response) =>
        {
            DisableResponseCaching(response);
            return Results.Ok(new WorkPlanningAvailability(options.Value.Enabled));
        });

        RouteGroupBuilder actions = api.MapGroup("").AddEndpointFilter(async (context, next) =>
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<WorkPlanningApiOptions>>();
            if (!options.Value.Enabled)
            {
                DisableResponseCaching(context.HttpContext.Response);
                return Results.Problem(title: "Work Planning disabled.",
                    detail: "Work Planning is disabled on this Dashboard.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?> { ["code"] = "work_planning_disabled" });
            }

            return await next(context);
        });

        actions.MapGet(
                "/daily-pick",
                GetDailyPickAsync)
            .WithName("GetMobileWorkPlanningDailyPick");

        actions.MapGet(
            "/random-pick",
            GetRandomPickAsync)
        .WithName("GetMobileWorkPlanningRandomPick");

        actions.MapGet(
                "/work-items",
                GetWorkItemsAsync)
            .WithName("GetMobileWorkPlanningWorkItems");

        actions.MapPost(
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
        bool? favorPriority,
        IWorkPlanningClient workPlanningClient,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            RandomWorkPick? pick = await workPlanningClient
                .GetRandomPickAsync(
                    favorPriority ?? false,
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
