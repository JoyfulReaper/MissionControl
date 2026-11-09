
using MissionControl.Archive.Storage.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace MissionControl.Archive.Endpoints;

public static class RandomSteamAnalyticsEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapRandomSteamAnalyticsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var apiKey = endpoints.ServiceProvider
            .GetRequiredService<IConfiguration>()["RandomSteamAnalytics:ApiKey"];

        // No configured key means no endpoint.
        if (string.IsNullOrWhiteSpace(apiKey))
            return endpoints;

        if (apiKey.Length is < 32 or > 256)
            throw new InvalidOperationException(
                "RandomSteamAnalytics:ApiKey must contain 32-256 characters.");

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

        endpoints.MapGet("/api/randomsteam/analytics/overview", async (
            HttpContext context,
            int? days,
            SqliteRandomSteamAnalyticsQuery query,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";

            var values = context.Request.Headers["X-RandomSteam-Analytics-Key"];

            if (values.Count != 1 ||
                values[0] is not { Length: >= 32 and <= 256 } providedKey)
            {
                return Results.Unauthorized();
            }

            var providedHash = SHA256.HashData(
                Encoding.UTF8.GetBytes(providedKey));

            if (!CryptographicOperations.FixedTimeEquals(expectedHash, providedHash))
                return Results.Unauthorized();

            var requestedDays = days ?? 30;

            if (requestedDays is < 1 or > 90)
                return Results.BadRequest("Days must be between 1 and 90.");

            // Exclude the current partial UTC day.
            var toUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            var fromUtc = toUtc.AddDays(-requestedDays);

            var overview = await query.GetOverviewAsync(
                fromUtc, toUtc, cancellationToken);

            return Results.Ok(overview);
        })
        .WithName("GetRandomSteamAnalyticsOverview")
        .WithTags("RandomSteam Analytics");

        return endpoints;
    }
}
