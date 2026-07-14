using MissionControl.Contracts;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace MissionControl.Tests;

public sealed class GitHubWebhookHttpTests
{
    [Fact]
    public async Task WebhookDisabledReturnsNotFound()
    {
        await using var factory = new GatewayTestApplicationFactory(
            new Dictionary<string, string?>
            {
                ["GitHubWebhook:Enabled"] = "false"
            });

        using var client = factory.CreateClient();
        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "ping",
                GitHubTestPayloads.PingBytes()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MissingSignatureReturnsUnauthorized()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();
        using var request = GitHubTestPayloads.SignedGitHubRequest(
            "ping",
            GitHubTestPayloads.PingBytes());
        request.Headers.Remove("X-Hub-Signature-256");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidSignatureReturnsUnauthorized()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "ping",
                GitHubTestPayloads.PingBytes(),
                signature: "sha256=abcdef"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AlteredBodyWithPreviouslyValidSignatureReturnsUnauthorized()
    {
        byte[] original = GitHubTestPayloads.PingBytes();
        byte[] altered = Encoding.UTF8.GetBytes("""{"zen":"changed"}""");

        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "ping",
                altered,
                signature: GitHubTestPayloads.Sign(original)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OversizedPayloadWithContentLengthReturnsPayloadTooLarge()
    {
        await using var factory = new GatewayTestApplicationFactory(
            new Dictionary<string, string?>
            {
                ["GitHubWebhook:MaxPayloadBytes"] = "8"
            });
        using var client = factory.CreateClient();
        byte[] body = Encoding.UTF8.GetBytes("""{"larger":true}""");

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest("ping", body));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task OversizedPayloadWithoutUsefulContentLengthReturnsPayloadTooLarge()
    {
        await using var factory = new GatewayTestApplicationFactory(
            new Dictionary<string, string?>
            {
                ["GitHubWebhook:MaxPayloadBytes"] = "8"
            });
        using var client = factory.CreateClient();
        byte[] body = Encoding.UTF8.GetBytes("""{"larger":true}""");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/webhooks/github")
        {
            Content = new UnknownLengthContent(body)
        };
        request.Headers.TryAddWithoutValidation("X-GitHub-Event", "ping");
        request.Headers.TryAddWithoutValidation("X-GitHub-Delivery", GitHubTestPayloads.DeliveryId.ToString());
        request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", GitHubTestPayloads.Sign(body));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData("event")]
    [InlineData("delivery")]
    [InlineData("invalid-delivery")]
    public async Task MissingOrInvalidGitHubHeadersReturnBadRequest(
        string scenario)
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();
        using var request = GitHubTestPayloads.SignedGitHubRequest(
            "ping",
            GitHubTestPayloads.PingBytes());

        if (scenario == "event")
        {
            request.Headers.Remove("X-GitHub-Event");
        }
        else if (scenario == "delivery")
        {
            request.Headers.Remove("X-GitHub-Delivery");
        }
        else
        {
            request.Headers.Remove("X-GitHub-Delivery");
            request.Headers.TryAddWithoutValidation("X-GitHub-Delivery", "not-a-guid");
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidSignedPingReturnsOkAndDoesNotPublish()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "ping",
                GitHubTestPayloads.PingBytes()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.Publisher.Events);
    }

    [Fact]
    public async Task UnsupportedSignedEventReturnsNoContentAndDoesNotPublish()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "issues",
                GitHubTestPayloads.UnsupportedEventBytes()));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(factory.Publisher.Events);
    }

    [Theory]
    [InlineData("tag")]
    [InlineData("deleted")]
    [InlineData("empty-commits")]
    public async Task IgnoredPushesReturnNoContentAndDoNotPublish(
        string scenario)
    {
        byte[] body = GitHubTestPayloads.PushBytes(
            root =>
            {
                if (scenario == "tag")
                {
                    root["ref"] = "refs/tags/v1.0.0";
                }
                else if (scenario == "deleted")
                {
                    root["deleted"] = true;
                }
                else
                {
                    root["commits"] = new JsonArray();
                    root["head_commit"] = null;
                }
            });

        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest("push", body));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(factory.Publisher.Events);
    }

    [Fact]
    public async Task WrongRepositoryOwnerReturnsForbiddenAndDoesNotPublish()
    {
        byte[] body = GitHubTestPayloads.PushBytes(
            root =>
                root["repository"]!["owner"]!["login"] = "SomeoneElse");

        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest("push", body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Publisher.Events);
    }

    [Fact]
    public async Task InvalidJsonReturnsBadRequestAndDoesNotPublish()
    {
        byte[] body = Encoding.UTF8.GetBytes("{");
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest("push", body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Publisher.Events);
    }

    [Theory]
    [MemberData(nameof(MalformedPushPayloads))]
    public async Task MalformedSignedPushPayloadsReturnBadRequestAndDoNotPublish(
        string _,
        byte[] body)
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest("push", body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Publisher.Events);
    }

    [Fact]
    public async Task ValidBranchPushPublishesNormalizedEnvelope()
    {
        byte[] body = GitHubTestPayloads.PushBytes(
            root =>
            {
                string longMessage = new('x', 520);
                root["commits"]![1]!["message"] = longMessage + "\nbody";
                root["head_commit"]![ "message"] = longMessage + "\nbody";
            });

        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest("push", body));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        IntegrationEventEnvelope envelope =
            Assert.Single(factory.Publisher.Events);
        Assert.Equal(GitHubTestPayloads.DeliveryId, envelope.EventId);
        Assert.Equal("github.push.received", envelope.EventType);
        Assert.Equal("github", envelope.Source);
        Assert.Equal(GitHubTestPayloads.DeliveryId.ToString(), envelope.CorrelationId);
        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal(GitHubTestPayloads.SecondCommitTimestamp, envelope.OccurredAt);

        JsonElement payload = envelope.Payload;
        Assert.Equal("dev", payload.GetProperty("branch").GetString());
        Assert.Equal("JoyfulReaper/MissionControl", payload.GetProperty("repository").GetString());
        Assert.Equal(123456789, payload.GetProperty("repositoryId").GetInt64());
        Assert.Equal(
            "https://github.com/JoyfulReaper/MissionControl",
            payload.GetProperty("repositoryUrl").GetString());
        Assert.Equal(2, payload.GetProperty("commitCount").GetInt32());

        JsonElement commits = payload.GetProperty("commits");
        Assert.Equal(2, commits.GetArrayLength());
        Assert.Equal("First commit line", commits[0].GetProperty("message").GetString());
        string secondMessage = commits[1].GetProperty("message").GetString()!;
        Assert.Equal(500, secondMessage.Length);
        Assert.False(commits[0].TryGetProperty("email", out _));
        Assert.False(commits[0].TryGetProperty("authorEmail", out _));
    }

    [Fact]
    public async Task LastCommitTimestampIsUsedWhenHeadCommitIsAbsent()
    {
        byte[] body = GitHubTestPayloads.PushBytes(
            root => root["head_commit"] = null);

        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest("push", body));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            GitHubTestPayloads.SecondCommitTimestamp,
            Assert.Single(factory.Publisher.Events).OccurredAt);
    }

    [Fact]
    public async Task PublisherFailureReturnsServiceUnavailable()
    {
        await using var factory = new GatewayTestApplicationFactory();
        factory.Publisher.FailureMode = PublisherFailureMode.ThrowException;
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "push",
                GitHubTestPayloads.PushBytes()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task RequestCancellationIsPropagated()
    {
        await using var factory = new GatewayTestApplicationFactory();
        factory.Publisher.FailureMode = PublisherFailureMode.WaitForCancellation;
        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync(
                GitHubTestPayloads.SignedGitHubRequest(
                    "push",
                    GitHubTestPayloads.PushBytes()),
                cts.Token));
    }

    [Fact]
    public async Task SameDeliveryGuidProducesSameEventIdAndDifferentDeliveryGuidsProduceDifferentEventIds()
    {
        byte[] body = GitHubTestPayloads.PushBytes();
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var first = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "push",
                body,
                GitHubTestPayloads.DeliveryId));
        using var second = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "push",
                body,
                GitHubTestPayloads.DeliveryId));
        using var third = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "push",
                body,
                GitHubTestPayloads.OtherDeliveryId));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, third.StatusCode);
        Assert.Equal(factory.Publisher.Events[0].EventId, factory.Publisher.Events[1].EventId);
        Assert.NotEqual(factory.Publisher.Events[0].EventId, factory.Publisher.Events[2].EventId);
    }

    public static IEnumerable<object[]> MalformedPushPayloads()
    {
        yield return ["empty object", Encoding.UTF8.GetBytes("{}")];
        yield return ["missing repository", Mutated(root => root.Remove("repository"))];
        yield return ["missing repository owner", Mutated(root => root["repository"]!.AsObject().Remove("owner"))];
        yield return ["missing repository owner login", Mutated(root => root["repository"]!["owner"]!.AsObject().Remove("login"))];
        yield return ["missing sender", Mutated(root => root.Remove("sender"))];
        yield return ["missing sender login", Mutated(root => root["sender"]!.AsObject().Remove("login"))];
        yield return ["missing ref", Mutated(root => root.Remove("ref"))];
        yield return ["null commits", Mutated(root => root["commits"] = null)];
        yield return ["null commit entry", Mutated(root => root["commits"]!.AsArray()[0] = null)];
        yield return ["commit missing message", Mutated(root => root["commits"]![0]!.AsObject().Remove("message"))];
        yield return ["commit missing id", Mutated(root => root["commits"]![0]!.AsObject().Remove("id"))];
        yield return ["commit missing url", Mutated(root => root["commits"]![0]!.AsObject().Remove("url"))];
        yield return ["default timestamp", Mutated(root => root["commits"]![0]!["timestamp"] = "0001-01-01T00:00:00+00:00")];
    }

    private static byte[] Mutated(Action<JsonObject> configure) =>
        GitHubTestPayloads.PushBytes(configure);

    private sealed class UnknownLengthContent(byte[] body) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await stream.WriteAsync(body);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
