extern alias DashboardApp;

using DashboardApp::MissionControl.Dashboard.GitActivity;
using Microsoft.Extensions.Options;
using System.Net;
using Xunit;

namespace MissionControl.Tests;

public sealed class DashboardGitActivityClientTests
{
    [Fact]
    public async Task ApiKeyHandlerAddsKeyAsHeaderNotUrl()
    {
        const string apiKey =
            "test-private-git-activity-key";
        var terminalHandler = new RecordingHandler();
        var keyHandler = new GitActivityApiKeyHandler(
            Options.Create(
                new GitActivityApiOptions
                {
                    Enabled = true,
                    BaseUrl = "http://gitactivity.internal/",
                    ApiKey = apiKey
                }))
        {
            InnerHandler = terminalHandler
        };
        using var client = new HttpClient(keyHandler)
        {
            BaseAddress = new Uri("http://gitactivity.internal/")
        };

        using HttpResponseMessage response =
            await client.GetAsync("api/github/activity?limit=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            apiKey,
            terminalHandler.ApiKey);
        Assert.DoesNotContain(
            apiKey,
            terminalHandler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task ApiKeyHandlerPropagatesCancellation()
    {
        var keyHandler = new GitActivityApiKeyHandler(
            Options.Create(
                new GitActivityApiOptions
                {
                    Enabled = true,
                    BaseUrl = "http://gitactivity.internal/",
                    ApiKey = "test-private-git-activity-key"
                }))
        {
            InnerHandler = new RecordingHandler()
        };
        using var client = new HttpClient(keyHandler)
        {
            BaseAddress = new Uri("http://gitactivity.internal/")
        };
        using var cancellationSource =
            new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync(
                "api/github/activity",
                cancellationSource.Token));
    }

    [Fact]
    public void OptionsRequireAbsoluteHttpUrlAndEnabledKey()
    {
        var validator =
            new GitActivityApiOptionsValidator();

        ValidateOptionsResult result = validator.Validate(
            null,
            new GitActivityApiOptions
            {
                Enabled = true,
                BaseUrl = "relative/path",
                ApiKey = ""
            });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("BaseUrl"));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("ApiKey"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues(
                GitActivityApiKeyHandler.HeaderName,
                out IEnumerable<string>? values)
                    ? Assert.Single(values)
                    : null;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
