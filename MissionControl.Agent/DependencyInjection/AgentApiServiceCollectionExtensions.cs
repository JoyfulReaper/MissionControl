using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace MissionControl.Agent.DependencyInjection;

internal static class AgentApiServiceCollectionExtensions
{
    internal const string CorsPolicyName =
        "AgentSnapshotCors";

    internal const string RateLimitPolicyName =
        "AgentSnapshotRateLimit";

    internal static IServiceCollection AddAgentApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection section =
            configuration.GetRequiredSection(
                AgentApiOptions.SectionName);

        services
            .AddOptions<AgentApiOptions>()
            .Bind(section)
            .Validate(
                options =>
                    options.StaleAfterSeconds > 0,
                "AgentApi:StaleAfterSeconds must be greater than zero.")
            .Validate(
                options =>
                    options.AllowedOrigins is { Length: > 0 },
                "AgentApi:AllowedOrigins must contain at least one origin.")
            .Validate(
                options =>
                    options.AllowedOrigins.All(IsValidOrigin),
                "Every Agent API allowed origin must be a valid HTTP or HTTPS origin.")
            .ValidateOnStart();

        AgentApiOptions apiOptions =
            section.Get<AgentApiOptions>() ??
            throw new InvalidOperationException(
                "AgentApi configuration is invalid.");

        services.AddCors(options =>
        {
            options.AddPolicy(
                CorsPolicyName,
                policy =>
                {
                    policy
                        .WithOrigins(apiOptions.AllowedOrigins)
                        .WithMethods(
                            HttpMethods.Get,
                            HttpMethods.Head)
                        .AllowAnyHeader();
                });
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode =
                StatusCodes.Status429TooManyRequests;

            options.AddFixedWindowLimiter(
                RateLimitPolicyName,
                limiterOptions =>
                {
                    limiterOptions.PermitLimit = 60;
                    limiterOptions.Window =
                        TimeSpan.FromMinutes(1);
                    limiterOptions.QueueLimit = 0;
                    limiterOptions.QueueProcessingOrder =
                        QueueProcessingOrder.OldestFirst;
                    limiterOptions.AutoReplenishment = true;
                });
        });

        return services;
    }

    private static bool IsValidOrigin(
        string origin)
    {
        if (string.IsNullOrWhiteSpace(origin) ||
            origin.Contains('*'))
        {
            return false;
        }

        if (!Uri.TryCreate(
                origin,
                UriKind.Absolute,
                out Uri? uri))
        {
            return false;
        }

        bool validScheme =
            uri.Scheme == Uri.UriSchemeHttp ||
            uri.Scheme == Uri.UriSchemeHttps;

        bool containsOnlyOrigin =
            uri.AbsolutePath == "/" &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) &&
            string.IsNullOrEmpty(uri.UserInfo);

        return validScheme &&
               containsOnlyOrigin;
    }
}