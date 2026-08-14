extern alias AgentApp;
extern alias GitActivityApp;

using AgentApp::MissionControl.Agent.DependencyInjection;
using AgentApp::MissionControl.Agent.Storage;
using GitActivityApp::MissionControl.GitActivity;
using GitActivityApp::MissionControl.GitActivity.DependencyInjection;
using GitActivityApp::MissionControl.GitActivity.Storage;
using GitActivityApp::MissionControl.GitActivity.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MissionControl.Tests;

public sealed class StorageConfigurationHardeningTests
{
    private const string ValidApiKey =
        "storage-tests-api-key-at-least-32-characters";

    [Fact]
    public void MissingAgentStorageSectionFailsRegistration()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(
            () => services.AddAgentSnapshotStorage(
                new ConfigurationBuilder().Build()));

        Assert.Contains("AgentStorage", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AgentDatabaseFileNameIsExplicitlyRequired(
        string? databaseFileName)
    {
        using var directory = new TemporaryDirectory();
        IConfiguration configuration = CreateAgentConfiguration(
            directory.Path,
            databaseFileName);
        using ServiceProvider provider = CreateAgentProvider(
            configuration);

        OptionsValidationException exception = Assert.Throws<
            OptionsValidationException>(
            () => provider.GetRequiredService<AgentDatabase>());

        Assert.Contains(
            "AgentStorage:DatabaseFileName is required.",
            exception.Failures);
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("nested/agent.db")]
    [InlineData("nested\\agent.db")]
    public void AgentDatabaseFileNameMustBeOnlyAFileName(
        string databaseFileName)
    {
        using var directory = new TemporaryDirectory();
        using ServiceProvider provider = CreateAgentProvider(
            CreateAgentConfiguration(
                directory.Path,
                databaseFileName));

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<AgentDatabase>());
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Theory]
    [InlineData("mission-control-agent.db")]
    [InlineData(".db")]
    [InlineData("agent-snapshot")]
    public void AgentValidFileNamesResolveToConfiguredBasePath(
        string databaseFileName)
    {
        using var directory = new TemporaryDirectory();
        using ServiceProvider provider = CreateAgentProvider(
            CreateAgentConfiguration(
                directory.Path,
                databaseFileName));

        AgentStorageOptions options = provider
            .GetRequiredService<IOptions<AgentStorageOptions>>()
            .Value;
        AgentDatabase database =
            provider.GetRequiredService<AgentDatabase>();

        string expectedPath = Path.GetFullPath(
            Path.Combine(directory.Path, databaseFileName));
        Assert.Equal(databaseFileName, options.DatabaseFileName);
        Assert.Equal(directory.Path, options.BasePath);
        Assert.Contains(
            expectedPath,
            database.ConnectionString,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public void AgentBasePathPointingToFileFailsWithoutCreatingDatabase()
    {
        using var directory = new TemporaryDirectory();
        string blocker = Path.Combine(directory.Path, "blocker");
        File.WriteAllText(blocker, "not a directory");
        using ServiceProvider provider = CreateAgentProvider(
            CreateAgentConfiguration(
                blocker,
                "agent.db"));

        OptionsValidationException exception = Assert.Throws<
            OptionsValidationException>(
            () => provider.GetRequiredService<AgentDatabase>());

        Assert.Contains(
            "AgentStorage:BasePath points to an existing file.",
            exception.Failures);
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public void AgentEnvironmentVariablesBindStoragePath()
    {
        using var directory = new TemporaryDirectory();
        string prefix = $"MC_AGENT_STORAGE_{Guid.NewGuid():N}_";
        string databaseVariable =
            $"{prefix}AgentStorage__DatabaseFileName";
        string basePathVariable =
            $"{prefix}AgentStorage__BasePath";

        try
        {
            Environment.SetEnvironmentVariable(
                databaseVariable,
                "environment-agent.db");
            Environment.SetEnvironmentVariable(
                basePathVariable,
                directory.Path);

            IConfiguration configuration =
                new ConfigurationBuilder()
                    .AddEnvironmentVariables(prefix)
                    .Build();
            using ServiceProvider provider =
                CreateAgentProvider(configuration);

            provider.GetRequiredService<AgentDatabase>();

            Assert.True(File.Exists(
                Path.Combine(
                    directory.Path,
                    "environment-agent.db")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                databaseVariable,
                null);
            Environment.SetEnvironmentVariable(
                basePathVariable,
                null);
        }
    }

    [Fact]
    public void AgentDirectoryCreationFailureIsSurfaced()
    {
        using var directory = new TemporaryDirectory();
        string blocker = Path.Combine(directory.Path, "blocker");
        File.WriteAllText(blocker, "not a directory");
        string impossibleDirectory = Path.Combine(blocker, "child");
        using ServiceProvider provider = CreateAgentProvider(
            CreateAgentConfiguration(
                impossibleDirectory,
                "agent.db"));

        Assert.ThrowsAny<IOException>(
            () => provider.GetRequiredService<AgentDatabase>());
        Assert.False(File.Exists(
            Path.Combine(impossibleDirectory, "agent.db")));
    }

    [Fact]
    public void MissingGitActivitySectionFailsRegistration()
    {
        var services = new ServiceCollection();

        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(
            () => services.AddGitActivity(
                new ConfigurationBuilder().Build()));

        Assert.Contains("GitActivity", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void GitActivityDatabaseFileNameIsExplicitlyRequired(
        string? databaseFileName)
    {
        using var directory = new TemporaryDirectory();
        using ServiceProvider provider = CreateGitActivityProvider(
            CreateGitActivityConfiguration(
                directory.Path,
                databaseFileName));

        OptionsValidationException exception = Assert.Throws<
            OptionsValidationException>(
            () => provider.GetRequiredService<GitActivityConnection>());

        Assert.Contains(
            "GitActivity:DatabaseFileName is required.",
            exception.Failures);
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("nested/git.db")]
    [InlineData("nested\\git.db")]
    public void GitActivityDatabaseFileNameMustBeOnlyAFileName(
        string databaseFileName)
    {
        using var directory = new TemporaryDirectory();
        using ServiceProvider provider = CreateGitActivityProvider(
            CreateGitActivityConfiguration(
                directory.Path,
                databaseFileName));

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<GitActivityConnection>());
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Theory]
    [InlineData("git-activity.db")]
    [InlineData(".sqlite")]
    [InlineData("activity")]
    public void GitActivityValidFileNamesResolveRealRepository(
        string databaseFileName)
    {
        using var directory = new TemporaryDirectory();
        using ServiceProvider provider = CreateGitActivityProvider(
            CreateGitActivityConfiguration(
                directory.Path,
                databaseFileName));

        GitActivityOptions options = provider
            .GetRequiredService<IOptions<GitActivityOptions>>()
            .Value;
        GitActivityConnection connection =
            provider.GetRequiredService<GitActivityConnection>();
        IGitActivityRepository repository =
            provider.GetRequiredService<IGitActivityRepository>();

        string expectedPath = Path.GetFullPath(
            Path.Combine(directory.Path, databaseFileName));
        Assert.Equal(databaseFileName, options.DatabaseFileName);
        Assert.IsType<SqliteGitActivityRepository>(repository);
        Assert.Contains(
            expectedPath,
            connection.ConnectionString,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(expectedPath));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("valid-api-key-at-least-32-characters", "0")]
    public void InvalidGitActivityOptionsDoNotInitializeStorage(
        string apiKey,
        string? defaultLimit = null)
    {
        using var directory = new TemporaryDirectory();
        var overrides = new Dictionary<string, string?>
        {
            ["GitActivity:ApiKey"] = apiKey
        };

        if (defaultLimit is not null)
        {
            overrides["GitActivity:DefaultResultLimit"] =
                defaultLimit;
        }

        using ServiceProvider provider = CreateGitActivityProvider(
            CreateGitActivityConfiguration(
                directory.Path,
                "git-activity.db",
                overrides));

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<GitActivityConnection>());
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public void GitActivityBasePathPointingToFileFails()
    {
        using var directory = new TemporaryDirectory();
        string blocker = Path.Combine(directory.Path, "blocker");
        File.WriteAllText(blocker, "not a directory");
        using ServiceProvider provider = CreateGitActivityProvider(
            CreateGitActivityConfiguration(
                blocker,
                "git-activity.db"));

        OptionsValidationException exception = Assert.Throws<
            OptionsValidationException>(
            () => provider.GetRequiredService<GitActivityConnection>());

        Assert.Contains(
            "GitActivity:BasePath points to an existing file.",
            exception.Failures);
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public void GitActivityEnvironmentVariablesBindStoragePath()
    {
        using var directory = new TemporaryDirectory();
        string prefix = $"MC_GIT_STORAGE_{Guid.NewGuid():N}_";
        var variables = new Dictionary<string, string?>
        {
            [$"{prefix}GitActivity__DatabaseFileName"] =
                "environment-git.db",
            [$"{prefix}GitActivity__BasePath"] = directory.Path,
            [$"{prefix}GitActivity__DefaultResultLimit"] = "10",
            [$"{prefix}GitActivity__MaxResultLimit"] = "50",
            [$"{prefix}GitActivity__ApiKey"] = ValidApiKey,
            [$"{prefix}GitActivity__AllowedRepositories__0"] =
                "JoyfulReaper/MissionControl",
            [$"{prefix}GitActivity__AllowedBranches__0"] = "dev",
            [$"{prefix}Nats__Url"] = "nats://localhost:4222",
            [$"{prefix}Nats__ClientName"] = "git-activity-tests",
            [$"{prefix}Nats__StreamName"] = "MISSION_CONTROL_EVENTS",
            [$"{prefix}NatsConsumer__DurableName"] = "mission-control-git-activity-tests",
            [$"{prefix}NatsConsumer__FilterSubject"] = "events.github.push.received",
            [$"{prefix}NatsConsumer__MaxDeliveries"] = "2"
        };

        try
        {
            foreach ((string variable, string? value) in variables)
            {
                Environment.SetEnvironmentVariable(variable, value);
            }

            IConfiguration configuration =
                new ConfigurationBuilder()
                    .AddEnvironmentVariables(prefix)
                    .Build();
            using ServiceProvider provider =
                CreateGitActivityProvider(configuration);

            provider.GetRequiredService<GitActivityConnection>();

            Assert.True(File.Exists(
                Path.Combine(
                    directory.Path,
                    "environment-git.db")));
        }
        finally
        {
            foreach (string variable in variables.Keys)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        }
    }

    private static IConfiguration CreateAgentConfiguration(
        string basePath,
        string? databaseFileName)
    {
        var values = new Dictionary<string, string?>
        {
            ["AgentStorage:BasePath"] = basePath
        };

        if (databaseFileName is not null)
        {
            values["AgentStorage:DatabaseFileName"] =
                databaseFileName;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IConfiguration CreateGitActivityConfiguration(
        string basePath,
        string? databaseFileName,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["GitActivity:BasePath"] = basePath,
            ["GitActivity:DefaultResultLimit"] = "10",
            ["GitActivity:MaxResultLimit"] = "50",
            ["GitActivity:ApiKey"] = ValidApiKey,
            ["GitActivity:AllowedRepositories:0"] =
                "JoyfulReaper/MissionControl",
            ["GitActivity:AllowedBranches:0"] = "dev",
            ["Nats:Url"] = "nats://localhost:4222",
            ["Nats:ClientName"] = "git-storage-tests",
            ["Nats:StreamName"] = "MISSION_CONTROL_EVENTS",
            ["NatsConsumer:DurableName"] = "mission-control-git-activity-tests",
            ["NatsConsumer:FilterSubject"] = "events.github.push.received",
            ["NatsConsumer:MaxDeliveries"] = "2"
        };

        if (databaseFileName is not null)
        {
            values["GitActivity:DatabaseFileName"] =
                databaseFileName;
        }

        if (overrides is not null)
        {
            foreach ((string key, string? value) in overrides)
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ServiceProvider CreateAgentProvider(
        IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddAgentSnapshotStorage(configuration);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateGitActivityProvider(
        IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitActivity(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"mission-control-storage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
