
extern alias ArchiveApp;

using ArchiveApp::MissionControl.Archive.Contracts;
using ArchiveApp::MissionControl.Archive.Endpoints;
using ArchiveApp::MissionControl.Archive.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MissionControl.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class RandomSteamAnalyticsEndpointTests
{
    private const string ApiKey = "test-randomsteam-analytics-key-0123456789";
    private const string Url = "/api/randomsteam/analytics/overview";

    static RandomSteamAnalyticsEndpointTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public async Task MissingConfiguration_DoesNotRegisterEndpoint()
    {
        await WithApiAsync(null, async (client, _) =>
        {
            using var response = await client.GetAsync(Url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("incorrect-randomsteam-analytics-key-1234567890")]
    public async Task MissingOrIncorrectKey_ReturnsUnauthorized(string? key)
    {
        await WithApiAsync(ApiKey, async (client, _) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url);

            if (key is not null)
                request.Headers.Add("X-RandomSteam-Analytics-Key", key);

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task InvalidDays_ReturnsBadRequest(int days)
    {
        await WithApiAsync(ApiKey, async (client, _) =>
        {
            using var request = AuthorizedRequest($"{Url}?days={days}");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        });
    }

    [Fact]
    public async Task ValidKey_ReturnsAggregatedArchiveData()
    {
        await WithApiAsync(ApiKey, async (client, archive) =>
        {
            var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            var yesterday = today.AddHours(-12);

            await AddEventAsync(archive, "randomsteam.site-visit.recorded",
                yesterday, new { visitorId = "visitor-1" });

            await AddEventAsync(archive, "randomsteam.game-pick.completed",
                yesterday, new { succeeded = true });

            await AddEventAsync(archive, "randomsteam.library-export.completed",
                yesterday, new { gameCount = 25 });

            using var request = AuthorizedRequest($"{Url}?days=1");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString());

            var overview = await response.Content.ReadFromJsonAsync<RandomSteamOverview>();

            Assert.NotNull(overview);
            Assert.Equal(1, overview.HomepageVisits);
            Assert.Equal(1, overview.IdentifiedVisitors);
            Assert.Equal(1, overview.PickerAttempts);
            Assert.Equal(1, overview.SuccessfulPicks);
            Assert.Equal(0, overview.FailedPicks);
            Assert.Equal(1, overview.CompletedExports);
        });
    }

    private static HttpRequestMessage AuthorizedRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-RandomSteam-Analytics-Key", ApiKey);
        return request;
    }

    private static async Task AddEventAsync(
        SqliteEventArchive archive,
        string eventType,
        DateTimeOffset occurredAt,
        object payload)
    {
        var envelope = new IntegrationEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: eventType,
            Source: "randomsteam",
            SchemaVersion: 1,
            OccurredAt: occurredAt,
            ReceivedAt: occurredAt,
            CorrelationId: null,
            CausationId: null,
            Payload: JsonSerializer.SerializeToElement(payload));

        Assert.True(await archive.StoreAsync(envelope));
    }

    private static async Task WithApiAsync(
        string? apiKey,
        Func<HttpClient, SqliteEventArchive, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"missioncontrol-analytics-api-{Guid.NewGuid():N}.db");

        try
        {
            var schemaType = typeof(SqliteEventArchive).Assembly.GetType(
                "MissionControl.Archive.Storage.Sqlite.SqliteEventArchiveSchema")
                ?? throw new InvalidOperationException("Archive schema not found.");

            var schema = (string?)schemaType.GetField(
                "Sql", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetRawConstantValue()
                ?? throw new InvalidOperationException("Archive schema SQL not found.");

            var connectionString = $"Data Source={path}";

            await using (var db = new SqliteConnection(connectionString))
            {
                await db.OpenAsync();
                await using var command = db.CreateCommand();
                command.CommandText = schema;
                await command.ExecuteNonQueryAsync();
            }

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RandomSteamAnalytics:ApiKey"] = apiKey
                });

            builder.Services.AddSingleton(
                new SqliteEventArchiveConnection(connectionString));
            builder.Services.AddSingleton<SqliteRandomSteamAnalyticsQuery>();

            await using var app = builder.Build();
            app.MapRandomSteamAnalyticsEndpoints();
            await app.StartAsync();

            using var client = app.GetTestClient();
            var archive = new SqliteEventArchive(
                app.Services.GetRequiredService<SqliteEventArchiveConnection>());

            await test(client, archive);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
