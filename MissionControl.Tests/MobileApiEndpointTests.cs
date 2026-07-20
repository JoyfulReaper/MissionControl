extern alias DashboardApp;

using DashboardApp::MissionControl.Dashboard.MobileApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionControl.Client.Archive;
using MissionControl.Contracts.Archive;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;

namespace MissionControl.Tests;

public sealed class MobileApiEndpointTests
{
    [Fact]
    public async Task ArchiveProxyDoesNotExposeInternalArchiveFailureDetails()
    {
        await using WebApplication app = CreateApplication(
            new FailingArchiveEventClient(
                new HttpRequestException(
                    "Connection refused at http://archive.internal:5191")));

        await app.StartAsync();

        using HttpClient client =
            app.GetTestClient();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "/api/events/statistics");

        request.Headers.Authorization =
            new("Test", "accepted");

        using HttpResponseMessage response =
            await client.SendAsync(request);

        string body =
            await response.Content.ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.BadGateway,
            response.StatusCode);
        Assert.Contains(
            "The Mission Control Archive could not be reached.",
            body);
        Assert.DoesNotContain(
            "archive.internal",
            body);
        Assert.DoesNotContain(
            "5191",
            body);
        Assert.DoesNotContain(
            "Connection refused",
            body);
    }

    private static WebApplication CreateApplication(
        IArchiveEventClient archiveClient)
    {
        WebApplicationBuilder builder =
            WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();

        builder.Services
            .AddAuthentication("Test")
            .AddScheme<
                AuthenticationSchemeOptions,
                TestAuthenticationHandler>(
                "Test",
                _ =>
                {
                });

        builder.Services
            .AddAuthorizationBuilder()
            .AddPolicy(
                MobileApiAuthenticationDefaults.Policy,
                policy =>
                {
                    policy.AddAuthenticationSchemes("Test");
                    policy.RequireAuthenticatedUser();
                })
            .SetFallbackPolicy(
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());

        builder.Services.AddSingleton(archiveClient);

        WebApplication app =
            builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMobileApiEndpoints();

        return app;
    }

    private sealed class FailingArchiveEventClient(
        Exception exception)
        : IArchiveEventClient
    {
        public Task<IReadOnlyList<ArchiveEventSummaryItem>> GetRecentAsync(
            int limit = 50,
            string? source = null,
            string? eventType = null,
            ArchiveEventCursor? before = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IReadOnlyList<ArchiveEventSummaryItem>>(
                exception);
        }

        public Task<ArchiveEventDetailsItem?> GetByIdAsync(
            Guid eventId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<ArchiveEventDetailsItem?>(
                exception);
        }

        public Task<ArchiveStatisticsItem> GetStatisticsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<ArchiveStatisticsItem>(
                exception);
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult>
            HandleAuthenticateAsync()
        {
            var identity =
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "Test Mobile Client")],
                    Scheme.Name);

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        new ClaimsPrincipal(identity),
                        Scheme.Name)));
        }
    }
}
