using MissionControl.Client.GitActivity;
using MissionControl.Contracts.GitActivity;
using System.Net;
using System.Text;
using Xunit;

namespace MissionControl.Tests;

public sealed class GitActivityClientTests
{
    [Fact]
    public async Task PrivateClientUsesBoundedExpectedRoute()
    {
        var handler = new RecordingHandler(
            CreateJsonResponse("[]"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://gitactivity.internal/")
        };
        var client = new GitActivityClient(
            httpClient,
            new GitActivityClientOptions(
                "api/github/activity"));

        await client.GetRecentAsync(500);

        Assert.Equal(
            "http://gitactivity.internal/api/github/activity?limit=50",
            handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task MobileClientUsesDashboardProxyWithoutPrivateHeader()
    {
        var handler = new RecordingHandler(
            CreateJsonResponse("[]"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://dashboard.example/")
        };
        var client = new GitActivityClient(
            httpClient,
            new GitActivityClientOptions(
                "api/mobile/git-activity"));

        await client.GetRecentAsync(20);

        Assert.Equal(
            "https://dashboard.example/api/mobile/git-activity?limit=20",
            handler.RequestUri?.AbsoluteUri);
        Assert.DoesNotContain(
            "X-Mission-Control-Key",
            handler.Headers);
    }

    [Fact]
    public async Task ClientRejectsMalformedResponse()
    {
        var handler = new RecordingHandler(
            CreateJsonResponse("{not-json"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://gitactivity.internal/")
        };
        var client = new GitActivityClient(
            httpClient,
            new GitActivityClientOptions(
                "api/github/activity"));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetRecentAsync());

        Assert.Equal(
            "The Git Activity response was malformed.",
            exception.Message);
    }

    [Fact]
    public async Task ClientPropagatesCancellation()
    {
        var handler = new RecordingHandler(
            CreateJsonResponse("[]"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://gitactivity.internal/")
        };
        var client = new GitActivityClient(
            httpClient,
            new GitActivityClientOptions(
                "api/github/activity"));
        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetRecentAsync(
                cancellationToken: cancellationSource.Token));
    }

    [Theory]
    [InlineData("https://other.example/api/github/activity")]
    [InlineData("//other.example/api/github/activity")]
    [InlineData("/api/github/activity")]
    [InlineData("\\\\other.example\\api\\github\\activity")]
    public async Task ClientRejectsNonRelativeRequestPath(
        string requestPath)
    {
        using var httpClient = new HttpClient(
            new RecordingHandler(CreateJsonResponse("[]")))
        {
            BaseAddress = new Uri("http://gitactivity.internal/")
        };
        var client = new GitActivityClient(
            httpClient,
            new GitActivityClientOptions(requestPath));

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetRecentAsync());

        Assert.Equal(
            "The Git Activity request path must be relative.",
            exception.Message);
    }

    [Fact]
    public async Task FeedSortsFiltersAndClearsWithoutMutatingLoadedItems()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GitActivityItem[] items =
        [
            CreateItem("Repo/B", "main", "bbbbbbbb", now),
            CreateItem("Repo/A", "dev", "aaaaaaaa", now.AddMinutes(1))
        ];
        var controller = new GitActivityFeedController(
            new StubGitActivityClient(items));

        await controller.LoadInitialAsync(CancellationToken.None);

        Assert.Equal("Repo/A", controller.Items[0].Repository);
        controller.RepositoryFilter = "repo/b";
        controller.BranchFilter = "MAIN";
        Assert.Equal(
            "Repo/B",
            Assert.Single(controller.FilteredItems).Repository);
        Assert.Equal(2, controller.Items.Count);

        controller.ClearFilters();

        Assert.False(controller.HasActiveFilters);
        Assert.Equal(2, controller.FilteredItems.Count);
    }

    [Fact]
    public async Task FeedRetainsOldDataWhenRefreshFails()
    {
        GitActivityItem item = CreateItem(
            "Repo/A",
            "main",
            "aaaaaaaa",
            DateTimeOffset.UtcNow);
        var client = new QueueGitActivityClient(
        [
            new[] { item },
            new HttpRequestException("private-host-secret")
        ]);
        var controller = new GitActivityFeedController(client)
        {
            RepositoryFilter = "Repo/A",
            BranchFilter = "main"
        };

        await controller.LoadInitialAsync(CancellationToken.None);
        await controller.RefreshAsync(CancellationToken.None);

        Assert.Equal(item, Assert.Single(controller.Items));
        Assert.Contains(
            "Latest Git Activity refresh failed",
            controller.RefreshWarning);
        Assert.DoesNotContain(
            "private-host-secret",
            controller.RefreshWarning);
        Assert.Equal("Repo/A", controller.RepositoryFilter);
        Assert.Equal("main", controller.BranchFilter);
    }

    private static GitActivityItem CreateItem(
        string repository,
        string branch,
        string sha,
        DateTimeOffset timestamp)
    {
        return new GitActivityItem(
            repository,
            branch,
            sha,
            "Commit message",
            "Author",
            "username",
            timestamp,
            "https://example.test/commit");
    }

    private static HttpResponseMessage CreateJsonResponse(
        string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class RecordingHandler(
        HttpResponseMessage response)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public HashSet<string> Headers { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;

            foreach (var header in request.Headers)
            {
                Headers.Add(header.Key);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class StubGitActivityClient(
        IReadOnlyList<GitActivityItem> items)
        : IGitActivityClient
    {
        public Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(items);
        }
    }

    private sealed class QueueGitActivityClient(
        IEnumerable<object> responses)
        : IGitActivityClient
    {
        private readonly Queue<object> responses = new(responses);

        public Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            object response = responses.Dequeue();

            return response is Exception exception
                ? Task.FromException<IReadOnlyList<GitActivityItem>>(
                    exception)
                : Task.FromResult(
                    (IReadOnlyList<GitActivityItem>)response);
        }
    }
}
