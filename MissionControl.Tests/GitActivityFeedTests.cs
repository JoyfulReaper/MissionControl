using MissionControl.Client.GitActivity;
using MissionControl.Contracts.GitActivity;
using MissionControl.UI.Components.GitActivity;
using Xunit;

namespace MissionControl.Tests;

public sealed class GitActivityFeedTests
{
    [Fact]
    public async Task FeedExposesLoadingEmptyStateAndPreventsOverlap()
    {
        var client = new BlockingGitActivityClient();
        var controller = new GitActivityFeedController(client);

        Task<bool> loading =
            controller.LoadInitialAsync(CancellationToken.None);
        await client.Started;

        Assert.True(controller.IsInitialLoading);
        Assert.True(controller.IsRefreshing);
        Assert.False(
            await controller.RefreshAsync(CancellationToken.None));

        client.Release([]);

        Assert.True(await loading);
        Assert.True(controller.HasLoaded);
        Assert.Empty(controller.Items);
        Assert.Null(controller.Error);
    }

    [Fact]
    public async Task FeedInitialFailureUsesSafeErrorState()
    {
        var controller = new GitActivityFeedController(
            new FailingGitActivityClient(
                new InvalidOperationException(
                    "secret-response-detail")));

        await controller.LoadInitialAsync(CancellationToken.None);

        Assert.Equal(
            "Git Activity could not be loaded. Try again.",
            controller.Error);
        Assert.DoesNotContain(
            "secret-response-detail",
            controller.Error);
        Assert.False(controller.HasLoaded);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/commit")]
    [InlineData("file:///private/commit")]
    public void InvalidCommitUrlsAreRejected(string value)
    {
        Assert.Null(
            GitActivityPresentation.GetSafeCommitUrl(value));
    }

    [Theory]
    [InlineData("https://example.test/commit")]
    [InlineData("http://example.test/commit")]
    public void HttpCommitUrlsAreAccepted(string value)
    {
        Assert.Equal(
            value,
            GitActivityPresentation.GetSafeCommitUrl(value));
    }

    private sealed class BlockingGitActivityClient
        : IGitActivityClient
    {
        private readonly TaskCompletionSource started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<
            IReadOnlyList<GitActivityItem>> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public async Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            return await release.Task.WaitAsync(cancellationToken);
        }

        public void Release(IReadOnlyList<GitActivityItem> items)
        {
            release.TrySetResult(items);
        }
    }

    private sealed class FailingGitActivityClient(
        Exception exception)
        : IGitActivityClient
    {
        public Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<
                IReadOnlyList<GitActivityItem>>(exception);
        }
    }
}
