using JoyfulReaperLib.Sqlite;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MissionControl.Dashboard.Agent;
using MissionControl.Dashboard.Archive;
using MissionControl.Dashboard.Authentication;
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
        AddDashboardAuthenticationStorage(
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

    private static void AddDashboardAuthenticationStorage(
        IServiceCollection services,
        IConfiguration configuration)
    {
        DashboardAuthenticationOptions options =
            configuration
                .GetSection(
                    DashboardAuthenticationOptions.SectionName)
                .Get<DashboardAuthenticationOptions>()
            ?? new DashboardAuthenticationOptions();

        string connectionString =
            SqliteDatabaseInitializer.Initialize(
                options.DatabaseFileName,
                DashboardAuthenticationSchema.Sql,
                options.BasePath);

        services.AddSingleton(
            new DashboardAuthenticationDatabase(
                connectionString));
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
            .AddOptions<DashboardAuthenticationOptions>()
            .Bind(
                configuration.GetSection(
                    DashboardAuthenticationOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(
                        options.DatabaseFileName),
                "Dashboard authentication database filename is required.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(
                        options.BasePath),
                "Dashboard authentication base path is required.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(
                        options.DataProtectionKeysPath),
                "Dashboard Data Protection key path is required.")
            .Validate(
                options =>
                    options.CookieLifetimeHours is >= 1 and <= 168,
                "Dashboard cookie lifetime must be between 1 and 168 hours.")
            .Validate(
                options =>
                    options.MaxFailedAttempts is >= 1 and <= 20,
                "Dashboard maximum failed attempts must be between 1 and 20.")
            .Validate(
                options =>
                    options.LockoutMinutes is >= 1 and <= 1440,
                "Dashboard lockout duration must be between 1 and 1440 minutes.")
            .ValidateOnStart();

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
