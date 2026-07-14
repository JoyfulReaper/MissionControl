using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace MissionControl.Tests;

public sealed class GenericEventsEndpointTests
{
    private static readonly Guid EventId =
        Guid.Parse("33333333-4444-5555-6666-777777777777");

    [Fact]
    public async Task MissingApiKeyReturnsUnauthorized()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/events",
            ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IncorrectApiKeyReturnsUnauthorized()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest();
        request.Headers.TryAddWithoutValidation(
            "X-Mission-Control-Key",
            "wrong-api-key-32-characters-long");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MultipleApiKeyHeaderValuesReturnUnauthorized()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest();
        request.Headers.TryAddWithoutValidation(
            "X-Mission-Control-Key",
            [GatewayTestApplicationFactory.EventSourceApiKey, GatewayTestApplicationFactory.EventSourceApiKey]);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidApiKeyPublishesEnvelopeWithResolvedSource()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest();
        request.Headers.TryAddWithoutValidation(
            "X-Mission-Control-Key",
            GatewayTestApplicationFactory.EventSourceApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var envelope = Assert.Single(factory.Publisher.Events);
        Assert.Equal(EventId, envelope.EventId);
        Assert.Equal("custom.event", envelope.EventType);
        Assert.Equal("configured-source", envelope.Source);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequestsReturnValidationErrors(
        object requestBody)
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/events")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.TryAddWithoutValidation(
            "X-Mission-Control-Key",
            GatewayTestApplicationFactory.EventSourceApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Publisher.Events);
    }

    [Fact]
    public async Task PublisherFailureReturnsServiceUnavailable()
    {
        await using var factory = new GatewayTestApplicationFactory();
        factory.Publisher.FailureMode = PublisherFailureMode.ThrowException;
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest();

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        await using var factory = new GatewayTestApplicationFactory();
        factory.Publisher.FailureMode = PublisherFailureMode.WaitForCancellation;
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync(request, cts.Token));
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [ValidRequest(eventId: Guid.Empty)];
        yield return [ValidRequest(eventType: "")];
        yield return [ValidRequest(schemaVersion: 0)];
        yield return [new
        {
            eventId = EventId,
            eventType = "custom.event",
            schemaVersion = 1,
            payload = new { ok = true }
        }];
        yield return [ValidRequest(payload: 42)];
    }

    private static HttpRequestMessage CreateAuthenticatedRequest()
    {
        var request = CreateRequest();
        request.Headers.TryAddWithoutValidation(
            "X-Mission-Control-Key",
            GatewayTestApplicationFactory.EventSourceApiKey);
        return request;
    }

    private static HttpRequestMessage CreateRequest() =>
        new(HttpMethod.Post, "/api/events")
        {
            Content = JsonContent.Create(ValidRequest())
        };

    private static object ValidRequest(
        Guid? eventId = null,
        string eventType = "custom.event",
        int schemaVersion = 1,
        object? payload = null)
    {
        return new
        {
            eventId = eventId ?? EventId,
            eventType,
            schemaVersion,
            occurredAt = "2026-07-13T12:00:00Z",
            correlationId = "correlation-1",
            payload = payload ?? new JsonObject
            {
                ["ok"] = true
            }
        };
    }
}
