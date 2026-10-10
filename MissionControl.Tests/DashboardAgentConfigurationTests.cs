extern alias DashboardApp;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using DashboardApp::MissionControl.Dashboard.Agents;
using DashboardApp::MissionControl.Dashboard.DependencyInjection;
using DashboardApp::MissionControl.Dashboard.GreenCloud;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkConfigurationIsRequiredOnlyWhenEnabled(bool enabled)
    {
        using var directory = new TestDirectory();
        WebApplicationBuilder builder = CreateBuilder(directory.Path);
        builder.Configuration["WorkPlanningApi:Enabled"] = enabled.ToString();
        builder.Configuration["WorkPlanningApi:BaseUrl"] = null;
        builder.Configuration["WorkPlanningApi:ApiKey"] = null;
        builder.Services.AddMissionControlDashboard(builder.Configuration);
        await using WebApplication app = builder.Build();
        if (enabled)
        {
            var exception = Assert.Throws<OptionsValidationException>(() => app.Services.GetRequiredService<IStartupValidator>().Validate());
            Assert.Contains("BaseUrl", exception.Message);
            Assert.Contains("key is required", exception.Message);
        }
        else
        {
            await app.StartAsync();
            var client = app.Services.GetRequiredService<MissionControl.Client.WorkPlanning.IWorkPlanningClient>();
            Assert.IsType<DashboardApp::MissionControl.Dashboard.WorkPlanning.DisabledWorkPlanningClient>(client);
            Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(DashboardApp::MissionControl.Dashboard.WorkPlanning.WorkPlanningApiKeyHandler));
            Assert.False((await app.Services.GetRequiredService<MissionControl.Client.WorkPlanning.IWorkPlanningAvailabilityClient>().GetAsync()).Enabled);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWorkItemsAsync());
            await app.StopAsync();
        }
    }

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

        Exception? exception = await Record.ExceptionAsync(() => app.StartAsync());
        Assert.NotNull(exception);
        IEnumerable<Exception> failures = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [exception];
        Assert.All(failures, failure =>
        {
            Assert.IsType<OptionsValidationException>(failure);
            Assert.Contains(error, failure.Message);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("obsolete-server-id")]
    public async Task GreenCloudEnabledWithoutMappingsFailsStartupValidation(string? legacyServerId)
    {
        using var directory = new TestDirectory();
        WebApplicationBuilder builder = CreateBuilder(directory.Path);
        builder.Configuration["GreenCloud:Enabled"] = "true";
        builder.Configuration["GreenCloud:ApiToken"] = "test-only-token";
        builder.Configuration["GreenCloud:ServerId"] = legacyServerId;
        builder.Services.AddMissionControlDashboard(builder.Configuration);
        await using WebApplication app = builder.Build();

        var exception = Assert.Throws<OptionsValidationException>(
            () => app.Services.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("explicit GreenCloud Servers mapping", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("obsolete-server-id")]
    public async Task GreenCloudCanonicalMappingsValidateAndLegacyKeyIsNotBound(string? legacyServerId)
    {
        using var directory = new TestDirectory();
        WebApplicationBuilder builder = CreateBuilder(directory.Path);
        ConfigureGreenCloudMappings(builder.Configuration);
        builder.Configuration["GreenCloud:ServerId"] = legacyServerId;
        builder.Services.AddMissionControlDashboard(builder.Configuration);
        await using WebApplication app = builder.Build();
        app.Services.GetRequiredService<IStartupValidator>().Validate();

        var options = app.Services.GetRequiredService<IOptions<GreenCloudOptions>>().Value;
        Assert.Equal(new[] { "first", "second" }, options.Servers.Select(server => server.NodeId));
        Assert.Equal(new[] { "provider-first", "provider-second" }, options.Servers.Select(server => server.ServerId));
    }

    [Theory]
    [InlineData("GreenCloud:Servers:0:NodeId", "")]
    [InlineData("GreenCloud:Servers:0:ServerId", " ")]
    [InlineData("GreenCloud:Servers:1:NodeId", "FIRST")]
    [InlineData("GreenCloud:Servers:1:ServerId", "PROVIDER-FIRST")]
    public async Task GreenCloudMappingsRequireNonemptyUniqueIds(string key, string value)
    {
        using var directory = new TestDirectory();
        WebApplicationBuilder builder = CreateBuilder(directory.Path);
        ConfigureGreenCloudMappings(builder.Configuration);
        builder.Configuration[key] = value;
        builder.Services.AddMissionControlDashboard(builder.Configuration);
        await using WebApplication app = builder.Build();
        Assert.Throws<OptionsValidationException>(
            () => app.Services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Theory]
    [InlineData("first", true)]
    [InlineData("FIRST", true)]
    [InlineData("unknown-node", false)]
    public async Task CatalogNodeReferencesValidateAgainstFleetAtStartup(string nodeId, bool valid)
    {
        using var directory = new TestDirectory();
        WebApplicationBuilder builder = CreateBuilder(directory.Path);
        builder.Configuration["ServiceCatalog:Services:0:NodeId"] = nodeId;
        builder.Services.AddMissionControlDashboard(builder.Configuration);
        await using WebApplication app = builder.Build();
        var validator = app.Services.GetRequiredService<IStartupValidator>();
        if (valid)
        {
            // The second configured node has neither services nor bandwidth mappings.
            validator.Validate();
        }
        else
        {
            var exception = Assert.Throws<OptionsValidationException>(validator.Validate);
            Assert.Contains("ServiceCatalog:Services service 'service'", exception.Message);
            Assert.Contains(nodeId, exception.Message);
            Assert.Contains("Agents:Nodes", exception.Message);
        }
    }

    [Theory]
    [InlineData("GreenCloud", true, "first", true)]
    [InlineData("GreenCloud", true, "FIRST", true)]
    [InlineData("GreenCloud", true, "unknown-node", false)]
    [InlineData("GreenCloud", false, "unknown-node", true)]
    [InlineData("GreenCloud", false, null, true)]
    [InlineData("Servitro", true, "first", true)]
    [InlineData("Servitro", true, "FIRST", true)]
    [InlineData("Servitro", true, "unknown-node", false)]
    [InlineData("Servitro", false, "unknown-node", true)]
    [InlineData("Servitro", false, null, true)]
    public async Task ProviderNodeReferencesValidateOnlyWhenEnabled(
        string provider, bool enabled, string? nodeId, bool valid)
    {
        using var directory = new TestDirectory();
        WebApplicationBuilder builder = CreateBuilder(directory.Path);
        builder.Configuration[$"{provider}:Enabled"] = enabled.ToString();
        builder.Configuration[$"{provider}:" + (provider == "GreenCloud" ? "ApiToken" : "Token")] = "test-only-token";
        if (nodeId is not null)
        {
            builder.Configuration[$"{provider}:Servers:0:NodeId"] = nodeId;
            builder.Configuration[$"{provider}:Servers:0:ServerId"] = "provider-server";
        }

        builder.Services.AddMissionControlDashboard(builder.Configuration);
        await using WebApplication app = builder.Build();
        var validator = app.Services.GetRequiredService<IStartupValidator>();
        if (valid)
        {
            validator.Validate();
        }
        else
        {
            var exception = Assert.Throws<OptionsValidationException>(validator.Validate);
            Assert.Contains($"{provider}:Servers[0]", exception.Message);
            Assert.Contains(nodeId!, exception.Message);
            Assert.Contains("Agents:Nodes", exception.Message);
        }
    }

    private static void ConfigureGreenCloudMappings(IConfiguration configuration)
    {
        configuration["GreenCloud:Enabled"] = "true";
        configuration["GreenCloud:ApiToken"] = "test-only-token";
        configuration["GreenCloud:Servers:0:NodeId"] = "first";
        configuration["GreenCloud:Servers:0:ServerId"] = "provider-first";
        configuration["GreenCloud:Servers:1:NodeId"] = "second";
        configuration["GreenCloud:Servers:1:ServerId"] = "provider-second";
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
                    null, null, null, [], [])
                {
                    NodeId = request.RequestUri.Host == "first.example" ? "first" : "second"
                })
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
