using Microsoft.Extensions.Options;
using MissionControl.Contracts;
using MissionControl.Gateway.Messaging;
using System.Buffers;
using System.Text.Json;

namespace MissionControl.Gateway.Integrations.GitHub;

public static class GitHubWebhookEndpoint
{
    private const string PushEventName = "push";
    private const string PingEventName = "ping";
    private const string BranchRefPrefix = "refs/heads/";

    private const string MissionControlEventType =
        "github.push.received";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static RouteHandlerBuilder MapGitHubWebhook(
        this IEndpointRouteBuilder endpoints)
    {
        return endpoints
            .MapPost(
                "/api/webhooks/github",
                HandleAsync)
            .WithName("ReceiveGitHubWebhook")
            .WithTags("Webhooks")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        IOptions<GitHubWebhookOptions> optionsAccessor,
        GitHubWebhookSignatureValidator signatureValidator,
        IEventPublisher publisher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        GitHubWebhookOptions options = optionsAccessor.Value;

        if (!options.Enabled)
        {
            return Results.NotFound();
        }

        ILogger logger =
            loggerFactory.CreateLogger("GitHubWebhook");

        byte[]? requestBody = await ReadBodyAsync(
            request,
            options.MaxPayloadBytes,
            cancellationToken);

        if (requestBody is null)
        {
            return Results.StatusCode(
                StatusCodes.Status413PayloadTooLarge);
        }

        string signature =
            request.Headers["X-Hub-Signature-256"].ToString();

        if (!signatureValidator.IsValid(
                requestBody,
                signature))
        {
            logger.LogDebug(
                "Rejected GitHub webhook with an invalid signature.");

            return Results.Unauthorized();
        }

        string eventName =
            request.Headers["X-GitHub-Event"].ToString();

        string deliveryHeader =
            request.Headers["X-GitHub-Delivery"].ToString();

        if (string.IsNullOrWhiteSpace(eventName) ||
            !Guid.TryParse(
                deliveryHeader,
                out Guid deliveryId))
        {
            return Results.BadRequest(
                new
                {
                    error =
                        "Required GitHub webhook headers are missing or invalid."
                });
        }

        if (eventName.Equals(
                PingEventName,
                StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Accepted GitHub webhook ping {DeliveryId}.",
                deliveryId);

            return Results.Ok(
                new
                {
                    status = "ok",
                    deliveryId
                });
        }

        if (!eventName.Equals(
                PushEventName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.NoContent();
        }

        GitHubPushWebhook? push;

        try
        {
            push = JsonSerializer.Deserialize<GitHubPushWebhook>(
                requestBody,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Unable to deserialize GitHub push webhook {DeliveryId}.",
                deliveryId);

            return Results.BadRequest(
                new { error = "Invalid GitHub push payload." });
        }

        if (!IsValidPushPayload(push))
        {
            return Results.BadRequest(
                new { error = "Incomplete GitHub push payload." });
        }

        var validPush = push!;
        var repository = validPush.Repository!;
        string ownerLogin = repository.Owner!.Login!;
        string pushRef = validPush.Ref!;
        var pushCommits = validPush.Commits!;
        string senderLogin = validPush.Sender!.Login!;

        if (!string.Equals(
                ownerLogin,
                options.AllowedOwner,
                StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Rejected GitHub delivery {DeliveryId} for repository {Repository}.",
                deliveryId,
                repository.FullName);

            return Results.StatusCode(
                StatusCodes.Status403Forbidden);
        }

        // Ignore tags, deleted branches and pushes containing no commits.
        if (validPush.Deleted ||
            !pushRef.StartsWith(
                BranchRefPrefix,
                StringComparison.Ordinal) ||
            pushCommits.Count == 0)
        {
            return Results.NoContent();
        }

        string branch =
            pushRef[BranchRefPrefix.Length..];

        GitHubCommitSummary[] commits =
            pushCommits
                .Select(
                    commit =>
                        new GitHubCommitSummary(
                            commit!.Id!,
                            FirstLine(commit.Message!),
                            commit.Author?.Name,
                            commit.Author?.Username,
                            commit.Timestamp!.Value,
                            commit.Url!))
                .ToArray();

        var eventPayload =
            new GitHubPushReceivedEvent(
                repository.FullName!,
                repository.Id!.Value,
                repository.HtmlUrl!,
                branch,
                pushRef,
                validPush.Before!,
                validPush.After!,
                validPush.Created,
                validPush.Forced,
                validPush.Pusher?.Name,
                senderLogin,
                validPush.CompareUrl,
                commits.Length,
                commits);

        DateTimeOffset receivedAt =
            DateTimeOffset.UtcNow;

        DateTimeOffset occurredAt =
            validPush.HeadCommit?.Timestamp ??
            commits[^1].Timestamp;

        var envelope =
            new IntegrationEventEnvelope(
                EventId: deliveryId,
                EventType: MissionControlEventType,
                Source: "github",
                SchemaVersion: 1,
                OccurredAt: occurredAt,
                ReceivedAt: receivedAt,
                CorrelationId: deliveryId.ToString(),
                CausationId: null,
                Payload: JsonSerializer.SerializeToElement(
                    eventPayload,
                    JsonOptions));

        try
        {
            await publisher.PublishAsync(
                envelope,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to publish GitHub delivery {DeliveryId}.",
                deliveryId);

            return Results.StatusCode(
                StatusCodes.Status503ServiceUnavailable);
        }

        logger.LogInformation(
            "Published GitHub push {DeliveryId} for {Repository}/{Branch} containing {CommitCount} commits.",
            deliveryId,
            repository.FullName,
            branch,
            commits.Length);

        return Results.Accepted(
            value: new
            {
                eventId = deliveryId,
                eventType = MissionControlEventType
            });
    }

    private static async Task<byte[]?> ReadBodyAsync(
        HttpRequest request,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > 0 &&
            request.ContentLength > maxBytes)
        {
            return null;
        }

        await using var output = new MemoryStream();

        byte[] buffer =
            ArrayPool<byte>.Shared.Rent(64 * 1024);

        try
        {
            int totalBytes = 0;

            while (true)
            {
                int bytesRemaining =
                    maxBytes - totalBytes + 1;

                if (bytesRemaining <= 0)
                {
                    return null;
                }

                int bytesRead =
                    await request.Body.ReadAsync(
                        buffer.AsMemory(
                            0,
                            Math.Min(
                                buffer.Length,
                                bytesRemaining)),
                        cancellationToken);

                if (bytesRead == 0)
                {
                    break;
                }

                totalBytes += bytesRead;

                if (totalBytes > maxBytes)
                {
                    return null;
                }

                output.Write(
                    buffer,
                    0,
                    bytesRead);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsValidPushPayload(
        GitHubPushWebhook? push)
    {
        if (push is null ||
            string.IsNullOrWhiteSpace(push.Ref) ||
            string.IsNullOrWhiteSpace(push.Before) ||
            string.IsNullOrWhiteSpace(push.After) ||
            push.Repository is null ||
            push.Repository.Id is null or <= 0 ||
            string.IsNullOrWhiteSpace(push.Repository.FullName) ||
            string.IsNullOrWhiteSpace(push.Repository.HtmlUrl) ||
            push.Repository.Owner is null ||
            string.IsNullOrWhiteSpace(push.Repository.Owner.Login) ||
            push.Sender is null ||
            string.IsNullOrWhiteSpace(push.Sender.Login) ||
            push.Commits is null)
        {
            return false;
        }

        return push.Commits.All(IsValidCommit) &&
            (push.HeadCommit is null ||
                IsValidCommit(push.HeadCommit));
    }

    private static bool IsValidCommit(
        GitHubWebhookCommit? commit)
    {
        return commit is not null &&
            !string.IsNullOrWhiteSpace(commit.Id) &&
            !string.IsNullOrWhiteSpace(commit.Message) &&
            !string.IsNullOrWhiteSpace(commit.Url) &&
            commit.Timestamp is not null &&
            commit.Timestamp.Value != default;
    }

    private static string FirstLine(string message)
    {
        string firstLine =
            message
                .ReplaceLineEndings("\n")
                .Split('\n', 2)[0]
                .Trim();

        const int maxLength = 500;

        return firstLine.Length <= maxLength
            ? firstLine
            : firstLine[..maxLength];
    }
}
