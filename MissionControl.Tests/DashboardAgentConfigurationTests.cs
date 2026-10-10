extern alias DashboardApp;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using DashboardApp::MissionControl.Dashboard.Agents;
using DashboardApp::MissionControl.Dashboard.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MissionControl.Client.Agent;
using MissionControl.Contracts.Agent;
using Xunit;

namespace MissionControl.Tests;

public sealed class DashboardAgentConfigurationTests
{
    [Fact]
    public async Task DashboardStartsWithoutLegacyUrlAndUsesEachConfiguredFleetNode()
    {
        using var directory = new TestDirectory();
        WebApplicationBuilder builder = CreateBuilder(directory.Path);
        Assert.Null(builder.Configuration["Agent:BaseUrl"]);
        builder.Services.AddMissionControlDashboard(builder.Configuration);
        var requests = new ConcurrentBag<Uri>();
        builder.Services.AddHttpClient(AgentFleetClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SnapshotHandler(requests));

        await using WebApplication app = builder.Build();
        await app.StartAsync();

        Assert.Null(app.Services.GetService<IAgentSnapshotClient>());
        var fleet = app.Services.GetRequiredService<IAgentFleetClient>();
        Assert.IsType<AgentFleetClient>(fleet);
        IReadOnlyList<AgentNodeResult> nodes = await fleet.GetSnapshotsAsync();
        Assert.Equal(new[] { "first", "second" }, nodes.Select(node => node.NodeId));
        Assert.Equal(new[] { "First", "Second" }, nodes.Select(node => node.DisplayName));
        Assert.All(nodes, node => Assert.True(node.Succeeded));
        Assert.Equal("first.example", nodes[0].Snapshot!.Node);
        Assert.Equal("second.example", nodes[1].Snapshot!.Node);
        Assert.Equal(
            new[] { "http://first.example/api/snapshot", "https://second.example/agent/api/snapshot" },
            requests.Select(uri => uri.AbsoluteUri).Order());

        await app.StopAsync();
    }

    [Theory]
    [InlineData("Agents:Nodes:1:NodeId", "FIRST", "unique")]
    [InlineData("Agents:Nodes:1:BaseUrl", "relative/path", "absolute HTTP or HTTPS")]
    public async Task DashboardStillValidatesFleetAtStartup(string key, string value, string error)
    {
        using var directory = new TestDirectory();
        WebApplicationBuilder builder = CreateBuilder(directory.Path);
        builder.Configuration[key] = value;
        builder.Services.AddMissionControlDashboard(builder.Configuration);
        await using WebApplication app = builder.Build();

        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => app.StartAsync());
        Assert.Contains(error, exception.Message);
    }

    private static WebApplicationBuilder CreateBuilder(string directory)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Archive:BaseUrl"] = "http://archive.example/",
            ["GitActivityApi:BaseUrl"] = "http://gitactivity.example/",
            ["Agents:Nodes:0:NodeId"] = "first",
            ["Agents:Nodes:0:DisplayName"] = "First",
            ["Agents:Nodes:0:BaseUrl"] = "http://first.example",
            ["Agents:Nodes:1:NodeId"] = "second",
            ["Agents:Nodes:1:DisplayName"] = "Second",
            ["Agents:Nodes:1:BaseUrl"] = "https://second.example/agent/",
            ["Dashboard:Authentication:BasePath"] = directory,
            ["Dashboard:Authentication:DataProtectionKeysPath"] = System.IO.Path.Combine(directory, "keys"),
            ["WorkPlanningApi:BaseUrl"] = "http://planning.example/",
            ["WorkPlanningApi:ApiKey"] = "test-only-key",
            ["ServiceCatalog:Services:0:Id"] = "service",
            ["ServiceCatalog:Services:0:Name"] = "Service",
            ["ServiceCatalog:Services:0:NodeId"] = "first",
            ["ServiceCatalog:Services:0:Group"] = "Tests",
            ["ServiceCatalog:Services:0:Summary"] = "Test service",
            ["ServiceCatalog:Services:0:Description"] = "Test service description",
            ["ServiceCatalog:Services:0:Visibility"] = "private"
        });
        return builder;
    }

    private sealed class SnapshotHandler(ConcurrentBag<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PublicNodeSnapshot(
                    request.RequestUri!.Host, DateTimeOffset.UtcNow, 0, false,
                    null, null, null, [], []))
            });
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"missioncontrol-dashboard-config-{Guid.NewGuid():N}");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
