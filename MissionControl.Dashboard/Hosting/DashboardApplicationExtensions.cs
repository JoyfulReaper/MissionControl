using MissionControl.Dashboard.Components;
using MissionControl.Dashboard.MobileApi;

namespace MissionControl.Dashboard.Hosting;

public static class DashboardApplicationExtensions
{
    public static WebApplication UseMissionControlDashboard(
        this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(
                "/Error",
                createScopeForErrors: true);

            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute(
            "/not-found",
            createScopeForStatusCodePages: true);

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseAntiforgery();
        app.MapRazorPages();
        app.MapMobileApiEndpoints();

        app.MapStaticAssets()
            .AllowAnonymous();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
