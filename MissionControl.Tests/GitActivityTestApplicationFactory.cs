extern alias GitActivityApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MissionControl.Contracts.GitActivity;
using MissionControl.Contracts.GitHub;
using MissionControl.Observability.RabbitMq;
using GitActivityProgram =
    GitActivityApp::Program;
using IGitActivityRepository =
    GitActivityApp::MissionControl.GitActivity.Storage.IGitActivityRepository;

namespace MissionControl.Tests;

internal sealed class GitActivityTestApplicationFactory
    : WebApplicationFactory<GitActivityProgram>
{
    internal const string ApiKey =
        "test-git-activity-api-key-32-characters";

    private readonly string _databaseDirectory =
        Path.Combine(
            Path.GetTempPath(),
            $"mission-control-git-activity-tests-{Guid.NewGuid():N}");

    public GitActivityTestApplicationFactory()
    {
        Directory.CreateDirectory(_databaseDirectory);
    }

    public StubGitActivityRepository Repository { get; } = new();

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["RabbitMq:HostName"] = "localhost",
                        ["RabbitMq:Port"] = "5672",
                        ["RabbitMq:UserName"] = "guest",
                        ["RabbitMq:Password"] = "guest",
                        ["RabbitMq:VirtualHost"] = "/",
                        ["RabbitMq:ClientProvidedName"] =
                            "git-activity-tests",

                        ["RabbitMqConsumer:ExchangeName"] =
                            "kgivler.events",
                        ["RabbitMqConsumer:QueueName"] =
                            "mission-control.git-activity.tests",
                        ["RabbitMqConsumer:RoutingKey"] =
                            "github.push.received",
                        ["RabbitMqConsumer:PrefetchCount"] = "10",

                        ["GitActivity:DatabaseFileName"] =
                            "git-activity.db",
                        ["GitActivity:BasePath"] =
                            _databaseDirectory,
                        ["GitActivity:DefaultResultLimit"] = "10",
                        ["GitActivity:MaxResultLimit"] = "50",
                        ["GitActivity:ApiKey"] = ApiKey,
                        ["GitActivity:AllowedRepositories:0"] =
                            "JoyfulReaper/MissionControl",
                        ["GitActivity:AllowedBranches:0"] = "dev"
                    });
            });

        builder.ConfigureServices(
            services =>
            {
                // Prevent the real RabbitMQ consumer from starting.
                services.RemoveAll<IHostedService>();

                services.RemoveAll<IGitActivityRepository>();
                services.RemoveAll<IRabbitMqConnectionStatus>();

                services.AddSingleton<IGitActivityRepository>(
                    Repository);

                services.AddSingleton<IRabbitMqConnectionStatus>(
                    new FakeRabbitMqConnectionStatus());
            });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_databaseDirectory))
        {
            Directory.Delete(
                _databaseDirectory,
                recursive: true);
        }
    }
}

internal sealed class StubGitActivityRepository
    : IGitActivityRepository
{
    public IReadOnlyList<GitActivityItem> Items { get; set; } = [];

    public int? LastRequestedLimit { get; private set; }

    public Task UpsertPushAsync(
        Guid pushEventId,
        DateTimeOffset receivedAt,
        GitHubPushReceivedEvent push,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        LastRequestedLimit = limit;

        IReadOnlyList<GitActivityItem> result =
            Items.Take(limit).ToArray();

        return Task.FromResult(result);
    }
}
