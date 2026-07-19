using Microsoft.Extensions.Logging;
using MissionControl.Client.Agent;

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
                fonts.AddFont(
                    "OpenSans-Regular.ttf",
                    "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        builder.Services.AddHttpClient<
            IAgentSnapshotClient,
            AgentSnapshotClient>(httpClient =>
            {
                httpClient.BaseAddress =
                    new Uri("https://status-api.kgivler.com/");

                httpClient.Timeout =
                    TimeSpan.FromSeconds(10);
            });

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}