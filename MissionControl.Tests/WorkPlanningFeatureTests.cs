extern alias DashboardApp;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MissionControl.Client.WorkPlanning;
using MissionControl.UI.Components.WorkPlanning;
using Xunit;

namespace MissionControl.Tests;

public sealed class WorkPlanningFeatureTests
{
    [Fact]
    public async Task DisabledDashboardPageAndNavigationNeedNoWorkClient()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IWorkPlanningAvailabilityClient>(new Availability(false));
        services.AddSingleton<NavigationManager>(new Navigation());
        using ServiceProvider provider = services.BuildServiceProvider();
        string page = await RenderAsync<DashboardApp::MissionControl.Dashboard.Components.Pages.Work>(provider);
        Assert.Contains("Work Planning is disabled", page);
        Assert.DoesNotContain("Quick add", page);
        string nav = await RenderAsync<DashboardApp::MissionControl.Dashboard.Components.Layout.NavMenu>(provider);
        Assert.DoesNotContain("href=\"work\"", nav);
        Assert.Contains("href=\"hosts\"", nav);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MobileOverviewNavigationAndPageGateInstantiatesContentOnlyWhenEnabled(bool enabled, bool directPage)
    {
        int contentLoads = 0;
        using var handler = new StatusHandler(enabled);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://dashboard.example/") };
        var services = new ServiceCollection().AddLogging();
        // The same authenticated status client used by Mobile; no Work-data client is registered.
        services.AddSingleton<IWorkPlanningAvailabilityClient>(new WorkPlanningAvailabilityClient(http));
        using ServiceProvider provider = services.BuildServiceProvider();
        RenderFragment content = builder =>
        {
            contentLoads++;
            builder.AddContent(0, "Work widget/navigation");
        };
        string markup = await RenderAsync<WorkPlanningFeature>(provider, new Dictionary<string, object?>
        {
            [nameof(WorkPlanningFeature.ChildContent)] = content,
            [nameof(WorkPlanningFeature.ShowUnavailable)] = directPage
        });
        Assert.Equal(enabled, markup.Contains("Work widget/navigation"));
        Assert.Equal(enabled ? 1 : 0, contentLoads);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(!enabled && directPage, markup.Contains("Work Planning is disabled"));
    }

    [Fact]
    public async Task UnreachableFeatureStatusIsNotReportedAsDisabled()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IWorkPlanningAvailabilityClient>(new Unavailable());
        using ServiceProvider provider = services.BuildServiceProvider();
        string markup = await RenderAsync<WorkPlanningFeature>(provider, new Dictionary<string, object?>
        {
            [nameof(WorkPlanningFeature.ShowUnavailable)] = true
        });
        Assert.Contains("could not be retrieved", markup);
        Assert.DoesNotContain("is disabled", markup);
        Assert.DoesNotContain("private", markup);
    }

    private static async Task<string> RenderAsync<T>(ServiceProvider services, Dictionary<string, object?>? values = null) where T : IComponent
    {
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<T>(values is null ? ParameterView.Empty : ParameterView.FromDictionary(values));
            return component.ToHtmlString();
        });
    }

    private sealed class Availability(bool enabled) : IWorkPlanningAvailabilityClient
    {
        public Task<WorkPlanningAvailability> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WorkPlanningAvailability(enabled));
    }

    private sealed class Unavailable : IWorkPlanningAvailabilityClient
    {
        public Task<WorkPlanningAvailability> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<WorkPlanningAvailability>(new HttpRequestException("private upstream details"));
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://dashboard.example/", "https://dashboard.example/");
    }

    private sealed class StatusHandler(bool enabled) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("/api/mobile/work-planning/status", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new WorkPlanningAvailability(enabled)) });
        }
    }
}
