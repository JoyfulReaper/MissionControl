
using MissionControl.Mcp;
using System.Net;
using System.Text;
using Xunit;

namespace MissionControl.Tests;

public sealed class McpArchiveClientTests
{
    [Fact]
    public async Task GetRecentEventsByTypeUsesEncodedServerSideFilter()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://archive.test/")
        };

        var client = new ArchiveClient(http);

        var result = await client.GetRecentEventsByTypeAsync(
            "  tcpnoise.connection+test  ", 20);

        Assert.Equal(
            "http://archive.test/api/events?limit=20&eventType=tcpnoise.connection%2Btest",
            handler.RequestUri?.AbsoluteUri);

        Assert.Equal(
            System.Text.Json.JsonValueKind.Array,
            result.ValueKind);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(500, 100)]
    public async Task GetRecentEventsByTypeClampsLimit(
        int requested,
        int expected)
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://archive.test/")
        };

        var client = new ArchiveClient(http);

        await client.GetRecentEventsByTypeAsync("test.event", requested);

        Assert.Contains(
            $"limit={expected}",
            handler.RequestUri?.Query);
    }

    [Fact]
    public async Task GetRecentEventsByTypeRejectsWhitespace()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://archive.test/")
        };

        var client = new ArchiveClient(http);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetRecentEventsByTypeAsync("   ", 10));

        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task GetRecentEventsByTypePropagatesHttpFailure()
    {
        using var handler = new RecordingHandler(
            HttpStatusCode.ServiceUnavailable);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://archive.test/")
        };

        var client = new ArchiveClient(http);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetRecentEventsByTypeAsync("test.event", 10));
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    "[]",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
