extern alias GitActivityApp;

using GitActivityApp::MissionControl.GitActivity;
using GitActivityApp::MissionControl.GitActivity.DependencyInjection;
using GitActivityApp::MissionControl.GitActivity.Processing;
using GitActivityApp::MissionControl.GitActivity.Storage;
using GitActivityApp::MissionControl.GitActivity.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MissionControl.Contracts;
using MissionControl.Contracts.GitHub;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class GitActivityAllowlistTests
{
    private const string ValidApiKey =
        "allowlist-tests-api-key-at-least-32-characters";

    [Fact]
    public void EmptyRepositoryAllowlistFails()
    {
        AssertValidationFailure(
            CreateOptions(repositories: []),
            "GitActivity:AllowedRepositories must contain at least one nonblank repository.");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BlankRepositoryEntryFails(string blankEntry)
    {
        AssertValidationFailure(
            CreateOptions(repositories: [blankEntry]),
            "GitActivity:AllowedRepositories contains a blank repository entry.");
        AssertValidationFailure(
            CreateOptions(
                repositories:
                [
                    "JoyfulReaper/MissionControl",
                    blankEntry
                ]),
            "GitActivity:AllowedRepositories contains a blank repository entry.");
    }

    [Fact]
    public void EmptyBranchAllowlistFails()
    {
        AssertValidationFailure(
            CreateOptions(branches: []),
            "GitActivity:AllowedBranches must contain at least one nonblank branch.");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BlankBranchEntryFails(string blankEntry)
    {
        AssertValidationFailure(
            CreateOptions(branches: [blankEntry]),
            "GitActivity:AllowedBranches contains a blank branch entry.");
        AssertValidationFailure(
            CreateOptions(branches: ["dev", blankEntry]),
            "GitActivity:AllowedBranches contains a blank branch entry.");
    }

    [Fact]
    public void TrimmedAndDuplicateEntriesAreValid()
    {
        GitActivityOptions options = CreateOptions(
            repositories:
            [
                " JoyfulReaper/MissionControl ",
                "joyfulreaper/missioncontrol"
            ],
            branches: [" dev ", "DEV"]);

        ValidateOptionsResult result =
            new GitActivityOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ProcessorUsesTrimmedCaseInsensitiveDistinctAllowlists()
    {
        var repository = new RecordingGitActivityRepository();
        var processor = new GitActivityEventProcessor(
            repository,
            Options.Create(
                CreateOptions(
                    repositories:
                    [
                        " JoyfulReaper/MissionControl ",
                        "joyfulreaper/missioncontrol"
                    ],
                    branches: [" dev ", "DEV"])),
            NullLogger<GitActivityEventProcessor>.Instance);

        await processor.ProcessAsync(
            CreatePushEnvelope(
                "JOYFULREAPER/MISSIONCONTROL",
                "Dev"));

        Assert.Equal(1, repository.UpsertCount);
        Assert.Equal(
            ["JoyfulReaper/MissionControl"],
            GetNormalizedValues(processor, "_allowedRepositories"));
        Assert.Equal(
            ["dev"],
            GetNormalizedValues(processor, "_allowedBranches"));
    }

    [Fact]
    public async Task ProcessorIgnoresDisallowedRepository()
    {
        var repository = new RecordingGitActivityRepository();
        var processor = CreateProcessor(repository);

        await processor.ProcessAsync(
            CreatePushEnvelope("JoyfulReaper/Other", "dev"));

        Assert.Equal(0, repository.UpsertCount);
    }

    [Fact]
    public async Task ProcessorIgnoresDisallowedBranch()
    {
        var repository = new RecordingGitActivityRepository();
        var processor = CreateProcessor(repository);

        await processor.ProcessAsync(
            CreatePushEnvelope(
                "JoyfulReaper/MissionControl",
                "main"));

        Assert.Equal(0, repository.UpsertCount);
    }

    [Fact]
    public void InvalidAllowlistDoesNotInitializeSqlite()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"mission-control-allowlist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            IConfiguration configuration =
                CreateServiceConfiguration(directory);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGitActivity(configuration);
            using ServiceProvider provider =
                services.BuildServiceProvider();

            OptionsValidationException exception = Assert.Throws<
                OptionsValidationException>(
                () => provider.GetRequiredService<
                    GitActivityConnection>());

            Assert.Contains(
                "GitActivity:AllowedRepositories contains a blank repository entry.",
                exception.Failures);
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static GitActivityEventProcessor CreateProcessor(
        IGitActivityRepository repository)
    {
        return new GitActivityEventProcessor(
            repository,
            Options.Create(CreateOptions()),
            NullLogger<GitActivityEventProcessor>.Instance);
    }

    private static GitActivityOptions CreateOptions(
        string[]? repositories = null,
        string[]? branches = null)
    {
        return new GitActivityOptions
        {
            DatabaseFileName = "git-activity.db",
            BasePath = Path.GetTempPath(),
            DefaultResultLimit = 10,
            MaxResultLimit = 50,
            ApiKey = ValidApiKey,
            AllowedRepositories = repositories ??
                ["JoyfulReaper/MissionControl"],
            AllowedBranches = branches ?? ["dev"]
        };
    }

    private static void AssertValidationFailure(
        GitActivityOptions options,
        string expectedFailure)
    {
        ValidateOptionsResult result =
            new GitActivityOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(expectedFailure, result.Failures);
    }

    private static string[] GetNormalizedValues(
        GitActivityEventProcessor processor,
        string fieldName)
    {
        FieldInfo field = typeof(GitActivityEventProcessor).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new Xunit.Sdk.XunitException(
                $"Processor field {fieldName} was not found.");
        var values = Assert.IsType<HashSet<string>>(
            field.GetValue(processor));

        return values.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IntegrationEventEnvelope CreatePushEnvelope(
        string repository,
        string branch)
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        var push = new GitHubPushReceivedEvent(
            Repository: repository,
            RepositoryId: 1,
            RepositoryUrl: "https://github.com/example/repository",
            Branch: branch,
            Ref: $"refs/heads/{branch}",
            Before: "before",
            After: "after",
            Created: false,
            Forced: false,
            Pusher: "pusher",
            Sender: "sender",
            CompareUrl: null,
            CommitCount: 1,
            Commits:
            [
                new GitHubCommitSummary(
                    Sha: "abc123",
                    Message: "Test commit",
                    Author: "Test Author",
                    AuthorUsername: "tester",
                    Timestamp: timestamp,
                    Url: "https://github.com/example/repository/commit/abc123")
            ]);

        return new IntegrationEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: "github.push.received",
            Source: "github",
            SchemaVersion: 1,
            OccurredAt: timestamp,
            ReceivedAt: timestamp,
            CorrelationId: null,
            CausationId: null,
            Payload: JsonSerializer.SerializeToElement(
                push,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web)));
    }

    private static IConfiguration CreateServiceConfiguration(
        string directory)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["GitActivity:DatabaseFileName"] =
                        "git-activity.db",
                    ["GitActivity:BasePath"] = directory,
                    ["GitActivity:DefaultResultLimit"] = "10",
                    ["GitActivity:MaxResultLimit"] = "50",
                    ["GitActivity:ApiKey"] = ValidApiKey,
                    ["GitActivity:AllowedRepositories:0"] = " ",
                    ["GitActivity:AllowedBranches:0"] = "dev",
                    ["Nats:Url"] = "nats://localhost:4222",
                    ["Nats:ClientName"] = "git-activity-tests",
                    ["Nats:StreamName"] = "MISSION_CONTROL_EVENTS",
                    ["NatsConsumer:DurableName"] = "mission-control-git-activity-tests",
                    ["NatsConsumer:FilterSubject"] = "events.github.push.received",
                    ["NatsConsumer:MaxDeliveries"] = "2"
                })
            .Build();
    }

    private sealed class RecordingGitActivityRepository
        : IGitActivityRepository
    {
        public int UpsertCount { get; private set; }

        public Task UpsertPushAsync(
            Guid pushEventId,
            DateTimeOffset receivedAt,
            GitHubPushReceivedEvent push,
            CancellationToken cancellationToken = default)
        {
            UpsertCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<
            MissionControl.Contracts.GitActivity.GitActivityItem>>
            GetRecentAsync(
                int limit,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<
                    MissionControl.Contracts.GitActivity.GitActivityItem>>(
                []);
        }
    }
}
