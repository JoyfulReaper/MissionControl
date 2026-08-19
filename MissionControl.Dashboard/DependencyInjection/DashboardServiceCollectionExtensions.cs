using JoyfulReaperLib.MissionControl;
using JoyfulReaperLib.Sqlite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MissionControl.Client.Agent;
using MissionControl.Client.Archive;
using MissionControl.Client.GitActivity;
using MissionControl.Client.WorkPlanning;
using MissionControl.Dashboard.Authentication;
using MissionControl.Dashboard.Configuration;
using MissionControl.Dashboard.Events;
using MissionControl.Dashboard.Formatting;
using MissionControl.Dashboard.GitActivity;
using MissionControl.Dashboard.MobileApi;
using MissionControl.Dashboard.Refresh;
using MissionControl.Dashboard.Security;
using MissionControl.Dashboard.Services;
using MissionControl.Dashboard.WorkPlanning;

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
        AddDashboardAuthentication(
            services,
            configuration);
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
        AddGitActivityClient(
            services,
            configuration);
        AddWorkPlanningClient(
            services,
            configuration);
        AddDashboardFormatting(services);
        AddMissionControlEventPublishing(
            services,
            configuration);
        return services;
    }

    private static void AddWorkPlanningClient(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<WorkPlanningApiOptions>()
            .Bind(
                configuration.GetSection(WorkPlanningApiOptions.SectionName))
            .Validate(
                options =>
                    Uri.TryCreate(
                        options.BaseUrl,
                        UriKind.Absolute,
                        out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp ||
                     uri.Scheme == Uri.UriSchemeHttps),
                "Work Planning API BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(
                        options.ApiKey),
                "Work Planning API key is required.")
            .ValidateOnStart();

        services.AddTransient<WorkPlanningApiKeyHandler>();

        services
            .AddHttpClient<
                IWorkPlanningClient,
                WorkPlanningClient>(
                (serviceProvider, httpClient) =>
                {
                    var options =
                        serviceProvider
                            .GetRequiredService<
                                IOptions<WorkPlanningApiOptions>>()
                            .Value;

                    httpClient.BaseAddress = CreateBaseUri(options.BaseUrl);
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                })
            .ConfigurePrimaryHttpMessageHandler(
                static () =>
                    new HttpClientHandler
                    {
                        AllowAutoRedirect = false
                    })
            .AddHttpMessageHandler<WorkPlanningApiKeyHandler>();
    }

    private static void AddMissionControlEventPublishing(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMissionControlClient(
            configuration.GetSection(MissionControlClientOptions.SectionName));

        services.AddScoped<DashboardLoginEventPublisher>();
    }

    private static void AddDashboardDataProtection(
        IServiceCollection services,
        DashboardAuthenticationOptions options)
    {
        string keysPath =
            Path.GetFullPath(
                options.DataProtectionKeysPath,
                AppContext.BaseDirectory);

        Directory.CreateDirectory(keysPath);

        services
            .AddDataProtection()
            .SetApplicationName("MissionControl.Dashboard")
            .PersistKeysToFileSystem(
                new DirectoryInfo(keysPath));
    }

    private static void AddDashboardAuthenticationStorage(
        IServiceCollection services,
        IConfiguration configuration)
    {
        DashboardAuthenticationOptions options =
            configuration
                .GetSection(DashboardAuthenticationOptions.SectionName)
                .Get<DashboardAuthenticationOptions>()
            ?? new DashboardAuthenticationOptions();

        AddDashboardDataProtection(
            services,
            options);

        string connectionString =
            SqliteDatabaseInitializer.Initialize(
                options.DatabaseFileName,
                DashboardAuthenticationSchema.Sql,
                options.BasePath);

        services.AddSingleton(new DashboardAuthenticationDatabase(connectionString));
        services.AddSingleton<IDashboardUserStore, SqliteDashboardUserStore>();
        services.AddSingleton<IPasswordHasher<DashboardUser>, PasswordHasher<DashboardUser>>();
        services.AddSingleton<DashboardUserProvisioningService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<DashboardPasswordAuthenticationService>();
    }

    private static void AddDashboardComponents(
        IServiceCollection services)
    {
        services.AddRazorPages();

        services
            .AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddCascadingAuthenticationState();
        services.AddSingleton<IDashboardPollingLoop, DashboardPollingLoop>();
    }

    private static void AddDashboardAuthentication(
        IServiceCollection services,
        IConfiguration configuration)
    {
        DashboardAuthenticationOptions authenticationOptions =
            configuration
                .GetSection(DashboardAuthenticationOptions.SectionName)
                .Get<DashboardAuthenticationOptions>()
            ?? new DashboardAuthenticationOptions();

        services
            .AddAuthentication(DashboardAuthenticationDefaults.Scheme)
            .AddCookie(
                DashboardAuthenticationDefaults.Scheme,
                options =>
                {
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
                    options.ReturnUrlParameter = "returnUrl";

                    options.ExpireTimeSpan = TimeSpan.FromHours(authenticationOptions.CookieLifetimeHours);
                    options.SlidingExpiration = true;

                    options.Cookie.Name = "__Host-MissionControl.Dashboard";
                    options.Cookie.Path = "/";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.IsEssential = true;

                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

                    options.EventsType = typeof(DashboardCookieAuthenticationEvents);
                })
            .AddScheme<MobileApiAuthenticationOptions,
                MobileApiAuthenticationHandler>(MobileApiAuthenticationDefaults.Scheme,
                _ =>
                {
                });

        services.AddScoped<DashboardCookieAuthenticationEvents>();

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                MobileApiAuthenticationDefaults.Policy,
                policy =>
                {
                    policy.AddAuthenticationSchemes(MobileApiAuthenticationDefaults.Scheme);
                    policy.RequireAuthenticatedUser();
                })
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
            .AddOptions<DashboardRefreshOptions>()
            .Bind(
                configuration.GetSection(DashboardRefreshOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<DashboardRefreshOptions>,
            DashboardRefreshOptionsValidator>();

        services
            .AddOptions<MobileApiAuthenticationOptions>(MobileApiAuthenticationDefaults.Scheme)
            .Bind(
                configuration.GetSection(MobileApiAuthenticationOptions.SectionName))
            .Validate(
                HasValidMobileApiTokenHash,
                "Dashboard Mobile API TokenHash must be a " +
                "Base64-encoded SHA-256 hash when the API is enabled.")
            .ValidateOnStart();

        services
            .AddOptions<DashboardAuthenticationOptions>()
            .Bind(
                configuration.GetSection(DashboardAuthenticationOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.DatabaseFileName),
                "Dashboard authentication database filename is required.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.BasePath),
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
                configuration.GetSection(ServiceCatalogOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<ServiceCatalogOptions>,
            ServiceCatalogOptionsValidator>();
        services.AddSingleton<
            IServiceCatalogMonitor,
            ConfigurationServiceCatalogMonitor>();

        services.AddSingleton<
            IValidateOptions<DashboardDateTimeOptions>,
            DashboardDateTimeOptionsValidator>();

        services
            .AddOptions<DashboardDateTimeOptions>()
            .Bind(configuration.GetSection(DashboardDateTimeOptions.SectionName))
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
                httpClient.BaseAddress = new Uri(archiveBaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(10);
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
                httpClient.BaseAddress = new Uri(agentBaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(10);
            });
    }

    private static void AddDashboardFormatting(IServiceCollection services)
    {
        services.AddSingleton<
            IDashboardDateTimeFormatter,
            DashboardDateTimeFormatter>();
    }

    private static void AddGitActivityClient(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<GitActivityApiOptions>()
            .Bind(configuration.GetSection(GitActivityApiOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<GitActivityApiOptions>,
            GitActivityApiOptionsValidator>();

        services.AddTransient<GitActivityApiKeyHandler>();
        services.AddSingleton(new GitActivityClientOptions("api/github/activity"));

        services
            .AddHttpClient<
                IGitActivityClient,
                GitActivityClient>((serviceProvider, httpClient) =>
                {
                    GitActivityApiOptions options =
                        serviceProvider
                            .GetRequiredService<
                                IOptions<GitActivityApiOptions>>()
                            .Value;

                    httpClient.BaseAddress = CreateBaseUri(options.BaseUrl);
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                })
            .ConfigurePrimaryHttpMessageHandler(
                static () =>
                    new HttpClientHandler
                    {
                        AllowAutoRedirect = false
                    })
            .AddHttpMessageHandler<GitActivityApiKeyHandler>();
    }

    private static Uri CreateBaseUri(string value)
    {
        string normalized = value.EndsWith(
            "/",
            StringComparison.Ordinal)
                ? value
                : $"{value}/";

        return new Uri(normalized, UriKind.Absolute);
    }

    private static bool HasValidMobileApiTokenHash(
        MobileApiAuthenticationOptions options)
    {
        if (!options.Enabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.TokenHash))
        {
            return false;
        }

        try
        {
            byte[] hash = Convert.FromBase64String(options.TokenHash);

            return hash.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
