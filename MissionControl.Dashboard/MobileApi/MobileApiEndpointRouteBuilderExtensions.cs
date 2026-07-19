namespace MissionControl.Dashboard.MobileApi;

public static class MobileApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMobileApiEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapGet(
                "/api/mobile/ping",
                static () =>
                    Results.Ok(
                        new
                        {
                            status = "ok"
                        }))
            .WithName("GetMobileApiPing")
            .WithTags("Mobile API")
            .RequireAuthorization(
                MobileApiAuthenticationDefaults.Policy);

        return endpoints;
    }
}