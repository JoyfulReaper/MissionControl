using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MissionControl.Dashboard.Agent;
using MissionControl.Dashboard.Archive;
using MissionControl.Dashboard.Formatting;
using MissionControl.Dashboard.Security;
using MissionControl.Dashboard.Services;

namespace MissionControl.Dashboard.DependencyInjection;

public static class DashboardServiceCollectionExtensions
{
    public static IServiceCollection AddMissionControlDashboard(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddDashboardComponents(services);
        AddDashboardAuthentication(services);
        AddDashboardOptions(
            services,
            configuration);
        AddArchiveClient(
            services,
            configuration);
        AddAgentClient(
            services,
            configuration);
        AddDashboardFormatting(services);

        return services;
    }

    private static void AddDashboardComponents(
        IServiceCollection services)
    {
        services
            .AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddCascadingAuthenticationState();
    }

    private static void AddDashboardAuthentication(
        IServiceCollection services)
    {
        services
            .AddAuthentication(
                DashboardAuthenticationDefaults.Scheme)
            .AddScheme<
                AuthenticationSchemeOptions,
                DashboardAuthenticationHandler>(
                DashboardAuthenticationDefaults.Scheme,
                _ => { });

        services
            .AddAuthorizationBuilder()
            .SetFallbackPolicy(
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());
    }

    private static void AddDashboardOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ServiceCatalogOptions>()
            .Bind(
                configuration.GetSection(
                    ServiceCatalogOptions.SectionName))
            .Validate(
                options => options.Services.Count > 0,
                "At least one dashboard service must be configured.")
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<DashboardDateTimeOptions>,
            DashboardDateTimeOptionsValidator>();

        services
            .AddOptions<DashboardDateTimeOptions>()
            .Bind(
                configuration.GetSection(
                    DashboardDateTimeOptions.SectionName))
            .ValidateOnStart();
    }

    private static void AddArchiveClient(
        IServiceCollection services,
        IConfiguration configuration)
    {
        string archiveBaseUrl =
            configuration["Archive:BaseUrl"]
            ?? throw new InvalidOperationException(
                "Archive:BaseUrl is not configured.");

        services.AddHttpClient<
            IArchiveEventClient,
            ArchiveEventClient>(httpClient =>
            {
                httpClient.BaseAddress =
                    new Uri(archiveBaseUrl);

                httpClient.Timeout =
                    TimeSpan.FromSeconds(10);
            });
    }

    private static void AddAgentClient(
        IServiceCollection services,
        IConfiguration configuration)
    {
        string agentBaseUrl =
            configuration["Agent:BaseUrl"]
            ?? throw new InvalidOperationException(
                "Agent:BaseUrl is not configured.");

        services.AddHttpClient<
            IAgentSnapshotClient,
            AgentSnapshotClient>(httpClient =>
            {
                httpClient.BaseAddress =
                    new Uri(agentBaseUrl);

                httpClient.Timeout =
                    TimeSpan.FromSeconds(10);
            });
    }

    private static void AddDashboardFormatting(
        IServiceCollection services)
    {
        services.AddSingleton<
            IDashboardDateTimeFormatter,
            DashboardDateTimeFormatter>();
    }
}
