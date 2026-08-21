using MissionControl.Client.Infrastructure;

namespace MissionControl.Dashboard.MobileApi;

public static class
    BandwidthMobileApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder
        MapBandwidthMobileApiEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapGet(
                "/api/mobile/bandwidth",
                HandleGetBandwidthAsync)
            .WithName(
                "GetMobileBandwidthUsage")
            .WithTags(
                "Mobile API",
                "Infrastructure")
            .Produces<BandwidthUsageSnapshot>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .RequireAuthorization(MobileApiAuthenticationDefaults.Policy);

        return endpoints;
    }

    private static async Task<IResult>
        HandleGetBandwidthAsync(
            IBandwidthUsageClient bandwidthClient,
            HttpResponse response,
            CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            BandwidthUsageSnapshot snapshot = await bandwidthClient.GetAsync(cancellationToken);

            return Results.Ok(snapshot);
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
            InvalidOperationException or
            System.Text.Json.JsonException;
    }

    private static IResult CreateUnavailableResult(
        Exception exception)
    {
        string detail =
            exception switch
            {
                TaskCanceledException =>
                    "The GreenCloud request timed out.",

                HttpRequestException =>
                    "GreenCloud could not be reached.",

                _ =>
                    "Bandwidth usage could not be retrieved."
            };

        return Results.Problem(
            title: "Bandwidth usage unavailable.",
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