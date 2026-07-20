extern alias GitActivityApp;

using System.Net;
using System.Net.Http.Json;
using MissionControl.Contracts.GitActivity;
using Xunit;

namespace MissionControl.Tests;

public sealed class GitActivityEndpointTests
{
    [Fact]
    public async Task MissingApiKeyReturnsUnauthorized()
    {
        await using var factory =
            new GitActivityTestApplicationFactory();

        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/github/activity");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Null(factory.Repository.LastRequestedLimit);
    }

    [Fact]
    public async Task IncorrectApiKeyReturnsUnauthorized()
    {
        await using var factory =
            new GitActivityTestApplicationFactory();

        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "/api/github/activity",
            "incorrect-api-key-32-characters-long");

        using var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Null(factory.Repository.LastRequestedLimit);
    }

    [Fact]
    public async Task ValidApiKeyReturnsRecentActivity()
    {
        await using var factory =
            new GitActivityTestApplicationFactory();

        factory.Repository.Items =
        [
            new GitActivityItem(
                Repository: "JoyfulReaper/MissionControl",
                Branch: "dev",
                Sha: "0123456789abcdef",
                Message: "Add GitActivity query endpoint",
                Author: "Kyle Givler",
                AuthorUsername: "JoyfulReaper",
                Timestamp: DateTimeOffset.Parse(
                    "2026-07-14T17:59:53Z"),
                Url: "https://example.test/commit")
        ];

        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "/api/github/activity",
            GitActivityTestApplicationFactory.ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var activity =
            await response.Content
                .ReadFromJsonAsync<GitActivityItem[]>();

        var item = Assert.Single(Assert.IsType<GitActivityItem[]>(
            activity));

        Assert.Equal(
            "JoyfulReaper/MissionControl",
            item.Repository);

        Assert.Equal("dev", item.Branch);
        Assert.Equal("0123456789abcdef", item.Sha);
        Assert.Equal(10, factory.Repository.LastRequestedLimit);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 50)]
    public async Task LimitIsClamped(
        int requestedLimit,
        int expectedLimit)
    {
        await using var factory =
            new GitActivityTestApplicationFactory();

        using var client = factory.CreateClient();
        using var request = CreateRequest(
            $"/api/github/activity?limit={requestedLimit}",
            GitActivityTestApplicationFactory.ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            expectedLimit,
            factory.Repository.LastRequestedLimit);
    }

    [Fact]
    public async Task MultipleApiKeyValuesReturnUnauthorized()
    {
        await using var factory =
            new GitActivityTestApplicationFactory();

        using var client = factory.CreateClient();
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "/api/github/activity");

        request.Headers.TryAddWithoutValidation(
            "X-Mission-Control-Key",
            [
                GitActivityTestApplicationFactory.ApiKey,
                GitActivityTestApplicationFactory.ApiKey
            ]);

        using var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    private static HttpRequestMessage CreateRequest(
        string path,
        string apiKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            path);

        request.Headers.TryAddWithoutValidation(
            "X-Mission-Control-Key",
            apiKey);

        return request;
    }
}
