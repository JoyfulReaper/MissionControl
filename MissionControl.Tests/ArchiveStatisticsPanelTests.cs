using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MissionControl.Contracts.Archive;
using MissionControl.UI.Components.Overview;
using Xunit;

namespace MissionControl.Tests;

public sealed class ArchiveStatisticsPanelTests
{
    [Fact]
    public async Task CategoryBarsUseWindowCountInsteadOfAllTimeCount()
    {
        var statistics = new ArchiveStatisticsItem(
            TotalEvents: 1000,
            EventsReceivedInWindow: 10,
            UniqueSources: 2,
            UniqueEventTypes: 2,
            LatestReceivedAt: null,
            TopSources: [new("source-a", 6)],
            TopEventTypes: [new("event-a", 2)]);

        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        await using var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>());

        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ArchiveStatisticsPanel.Statistics)] = statistics
                });

            var component = await renderer.RenderComponentAsync<ArchiveStatisticsPanel>(parameters);
            return component.ToHtmlString();
        });

        Assert.Contains("width: 60.00%", html);
        Assert.Contains("width: 20.00%", html);
        Assert.DoesNotContain("width: 0.60%", html);
    }
}