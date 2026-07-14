using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MissionControl.Tests;

internal static class GitHubTestPayloads
{
    internal static readonly Guid DeliveryId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    internal static readonly Guid OtherDeliveryId =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    internal static readonly DateTimeOffset FirstCommitTimestamp =
        DateTimeOffset.Parse("2026-07-13T12:00:00Z");

    internal static readonly DateTimeOffset SecondCommitTimestamp =
        DateTimeOffset.Parse("2026-07-13T12:05:00Z");

    internal static byte[] PingBytes() =>
        Encoding.UTF8.GetBytes("""{"zen":"Approachable is better than simple."}""");

    internal static byte[] UnsupportedEventBytes() =>
        Encoding.UTF8.GetBytes("""{"action":"opened"}""");

    internal static byte[] PushBytes(
        Action<JsonObject>? configure = null)
    {
        var root = JsonNode.Parse(
            $$"""
            {
              "ref": "refs/heads/dev",
              "before": "0000000000000000000000000000000000000000",
              "after": "2222222222222222222222222222222222222222",
              "created": false,
              "deleted": false,
              "forced": false,
              "compare": "https://github.com/JoyfulReaper/MissionControl/compare/0000000...2222222",
              "repository": {
                "id": 123456789,
                "full_name": "JoyfulReaper/MissionControl",
                "html_url": "https://github.com/JoyfulReaper/MissionControl",
                "owner": {
                  "login": "JoyfulReaper"
                }
              },
              "pusher": {
                "name": "JoyfulReaper",
                "email": "pusher@example.test"
              },
              "sender": {
                "login": "JoyfulReaper"
              },
              "commits": [
                {
                  "id": "1111111111111111111111111111111111111111",
                  "message": "First commit line\n\nBody that should not be retained",
                  "timestamp": "{{FirstCommitTimestamp:O}}",
                  "url": "https://github.com/JoyfulReaper/MissionControl/commit/1111111",
                  "author": {
                    "name": "Kyle",
                    "email": "author@example.test",
                    "username": "JoyfulReaper"
                  }
                },
                {
                  "id": "2222222222222222222222222222222222222222",
                  "message": "Second commit line",
                  "timestamp": "{{SecondCommitTimestamp:O}}",
                  "url": "https://github.com/JoyfulReaper/MissionControl/commit/2222222",
                  "author": {
                    "name": "Kyle",
                    "email": "author@example.test",
                    "username": "JoyfulReaper"
                  }
                }
              ],
              "head_commit": {
                "id": "2222222222222222222222222222222222222222",
                "message": "Second commit line",
                "timestamp": "{{SecondCommitTimestamp:O}}",
                "url": "https://github.com/JoyfulReaper/MissionControl/commit/2222222",
                "author": {
                  "name": "Kyle",
                  "email": "author@example.test",
                  "username": "JoyfulReaper"
                }
              }
            }
            """)!.AsObject();

        configure?.Invoke(root);

        return JsonSerializer.SerializeToUtf8Bytes(
            root,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    internal static HttpRequestMessage SignedGitHubRequest(
        string eventName,
        byte[] body,
        Guid? deliveryId = null,
        string? signature = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/webhooks/github")
        {
            Content = new ByteArrayContent(body)
        };

        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");

        request.Headers.TryAddWithoutValidation(
            "X-GitHub-Event",
            eventName);
        request.Headers.TryAddWithoutValidation(
            "X-GitHub-Delivery",
            (deliveryId ?? DeliveryId).ToString());
        request.Headers.TryAddWithoutValidation(
            "X-Hub-Signature-256",
            signature ?? Sign(body));

        return request;
    }

    internal static string Sign(
        byte[] body,
        string secret = GatewayTestApplicationFactory.WebhookSecret)
    {
        byte[] digest =
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                body);

        return $"sha256={Convert.ToHexString(digest).ToLowerInvariant()}";
    }
}
