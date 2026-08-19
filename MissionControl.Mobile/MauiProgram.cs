using Microsoft.Extensions.Logging;
using MissionControl.Client.Agent;
using MissionControl.Client.Archive;
using MissionControl.Client.GitActivity;
using MissionControl.Client.Infrastructure;
using MissionControl.Client.WorkPlanning;
using MissionControl.Mobile.Services;

namespace MissionControl.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        builder.Services.AddHttpClient<IAgentSnapshotClient,
            AgentSnapshotClient>(httpClient =>
            {
                httpClient.BaseAddress = new Uri("https://status-api.kgivler.com/");
                httpClient.Timeout = TimeSpan.FromSeconds(10);
            });

        builder.Services.AddSingleton<MobileApiCredentialStore>();
        builder.Services.AddTransient<MobileApiAuthorizationHandler>();
        builder.Services.AddSingleton(new GitActivityClientOptions("api/mobile/git-activity"));
        builder.Services.AddSingleton(new WorkPlanningClientOptions("api/mobile/work-planning/"));

        builder.Services.AddHttpClient<MobileApiConnectionClient>(ConfigureMobileApiClient)
            .AddHttpMessageHandler<MobileApiAuthorizationHandler>();

        builder.Services.AddHttpClient<IArchiveEventClient, ArchiveEventClient>(ConfigureMobileApiClient)
            .AddHttpMessageHandler<MobileApiAuthorizationHandler>();

        builder.Services.AddHttpClient<IGitActivityClient, GitActivityClient>(ConfigureMobileApiClient)
            .AddHttpMessageHandler<MobileApiAuthorizationHandler>();

        builder.Services.AddHttpClient<IWorkPlanningClient, WorkPlanningClient>(ConfigureMobileApiClient)
            .AddHttpMessageHandler<MobileApiAuthorizationHandler>();

        builder.Services.AddHttpClient<IBandwidthUsageClient, BandwidthUsageClient>(ConfigureMobileApiClient)
            .AddHttpMessageHandler<MobileApiAuthorizationHandler>();

        builder.Services.AddSingleton<MobileServiceCatalog>();
        builder.Services.AddSingleton<MobileAgentSnapshotState>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void ConfigureMobileApiClient(
        HttpClient httpClient)
    {
        httpClient.BaseAddress = new Uri("https://dashboard.kgivler.com/");
        httpClient.Timeout = TimeSpan.FromSeconds(15);
    }
}
