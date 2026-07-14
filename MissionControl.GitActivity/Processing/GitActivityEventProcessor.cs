/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

using Microsoft.Extensions.Options;
using MissionControl.Contracts;
using MissionControl.Contracts.GitHub;
using MissionControl.GitActivity.Storage;
using MissionControl.Messaging.RabbitMq;
using System.Text.Json;

namespace MissionControl.GitActivity.Processing;

public sealed class GitActivityEventProcessor
    : IIntegrationEventProcessor
{
    private const string GitHubPushEventType =
        "github.push.received";

    private const string GitHubSource = "github";

    private const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IGitActivityRepository _repository;
    private readonly ILogger<GitActivityEventProcessor> _logger;
    private readonly HashSet<string> _allowedRepositories;
    private readonly HashSet<string> _allowedBranches;

    public GitActivityEventProcessor(
        IGitActivityRepository repository,
        IOptions<GitActivityOptions> options,
        ILogger<GitActivityEventProcessor> logger)
    {
        _repository = repository;
        _logger = logger;

        _allowedRepositories = options.Value
            .AllowedRepositories
            .Where(repositoryName =>
                !string.IsNullOrWhiteSpace(repositoryName))
            .Select(repositoryName => repositoryName.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _allowedBranches = options.Value
            .AllowedBranches
            .Where(branch =>
                !string.IsNullOrWhiteSpace(branch))
            .Select(branch => branch.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task ProcessAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (!string.Equals(
                integrationEvent.EventType,
                GitHubPushEventType,
                StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Ignoring unexpected event type {EventType} with ID {EventId}.",
                integrationEvent.EventType,
                integrationEvent.EventId);

            return;
        }

        if (!string.Equals(
                integrationEvent.Source,
                GitHubSource,
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Ignoring GitHub push event {EventId} from unexpected source {Source}.",
                integrationEvent.EventId,
                integrationEvent.Source);

            return;
        }

        if (integrationEvent.SchemaVersion !=
            SupportedSchemaVersion)
        {
            throw new NotSupportedException(
                $"GitHub push event schema version " +
                $"{integrationEvent.SchemaVersion} is not supported.");
        }

        var push = integrationEvent.Payload
            .Deserialize<GitHubPushReceivedEvent>(JsonOptions)
            ?? throw new JsonException(
                "GitHub push payload deserialized to null.");

        if (!_allowedRepositories.Contains(push.Repository))
        {
            _logger.LogDebug(
                "Ignoring push event {EventId} for repository {Repository}.",
                integrationEvent.EventId,
                push.Repository);

            return;
        }

        if (!_allowedBranches.Contains(push.Branch))
        {
            _logger.LogDebug(
                "Ignoring push event {EventId} for branch {Branch} in repository {Repository}.",
                integrationEvent.EventId,
                push.Branch,
                push.Repository);

            return;
        }

        await _repository.UpsertPushAsync(
            integrationEvent.EventId,
            integrationEvent.ReceivedAt,
            push,
            cancellationToken);

        _logger.LogInformation(
            "Stored {CommitCount} commits from {Repository}/{Branch} for event {EventId}.",
            push.Commits.Count,
            push.Repository,
            push.Branch,
            integrationEvent.EventId);
    }
}