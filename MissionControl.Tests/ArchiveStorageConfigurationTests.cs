extern alias ArchiveApp;
using ArchiveApp::MissionControl.Archive.DependencyInjection;
using ArchiveApp::MissionControl.Archive.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MissionControl.Messaging.Nats;
using System.Text;
using Xunit;

namespace MissionControl.Tests;

public sealed class ArchiveStorageConfigurationTests
{
    [Fact]
    public void MissingEventArchiveSectionFailsWithoutRegisteringStorage()
    {
        var services = new ServiceCollection();
        IConfiguration configuration =
            new ConfigurationBuilder().Build();

        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(
                () => services.AddMissionControlArchive(configuration));

        Assert.Contains("EventArchive", exception.Message);
        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType ==
                typeof(SqliteEventArchiveConnection));
    }

    [Fact]
    public void MisspelledEventArchiveSectionFails()
    {
        IConfiguration configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new("EventAchive:DatabaseFileName", "archive.db")
                ])
                .Build();

        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(
                () => new ServiceCollection()
                    .AddMissionControlArchive(configuration));

        Assert.Contains("EventArchive", exception.Message);
    }

    [Fact]
    public void EmptyEventArchiveSectionFails()
    {
        IConfiguration configuration = ConfigurationFromJson(
            """
            {
              "EventArchive": {}
            }
            """);

        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(
                () => new ServiceCollection()
                    .AddMissionControlArchive(configuration));

        Assert.Contains("EventArchive", exception.Message);
    }

    [Fact]
    public void ExistingValidJsonConfigurationInitializesArchive()
    {
        string directory = CreateTempDirectory();

        try
        {
            IConfiguration configuration = ConfigurationFromJson(
                $$"""
                {
                  "EventArchive": {
                    "DatabaseFileName": "mission-control.db",
                    "BasePath": {{System.Text.Json.JsonSerializer.Serialize(directory)}}
                  }
                }
                """);

            using ServiceProvider provider =
                BuildProvider(configuration);

            string dataSource = GetDataSource(provider);

            Assert.Equal(
                Path.Combine(directory, "mission-control.db"),
                dataSource);
            Assert.True(File.Exists(dataSource));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void EnvironmentVariablesBindArchiveSettings()
    {
        string directory = CreateTempDirectory();
        const string databaseVariable =
            "EventArchive__DatabaseFileName";
        const string basePathVariable =
            "EventArchive__BasePath";
        string? originalDatabaseFileName =
            Environment.GetEnvironmentVariable(databaseVariable);
        string? originalBasePath =
            Environment.GetEnvironmentVariable(basePathVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                databaseVariable,
                "environment.db");
            Environment.SetEnvironmentVariable(
                basePathVariable,
                directory);

            IConfiguration configuration =
                new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();

            using ServiceProvider provider =
                BuildProvider(configuration);

            Assert.Equal(
                Path.Combine(directory, "environment.db"),
                GetDataSource(provider));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                databaseVariable,
                originalDatabaseFileName);
            Environment.SetEnvironmentVariable(
                basePathVariable,
                originalBasePath);
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void EnvironmentVariablesOverrideJsonSettings()
    {
        string directory = CreateTempDirectory();
        string prefix = $"MC_ARCHIVE_{Guid.NewGuid():N}_";

        try
        {
            Environment.SetEnvironmentVariable(
                $"{prefix}EventArchive__DatabaseFileName",
                "override.db");
            Environment.SetEnvironmentVariable(
                $"{prefix}EventArchive__BasePath",
                directory);

            IConfiguration configuration =
                new ConfigurationBuilder()
                    .AddJsonStream(
                        new MemoryStream(
                            Encoding.UTF8.GetBytes(
                                """
                                {
                                  "EventArchive": {
                                    "DatabaseFileName": "json.db",
                                    "BasePath": "json-data"
                                  }
                                }
                                """)))
                    .AddEnvironmentVariables(prefix)
                    .Build();

            using ServiceProvider provider =
                BuildProvider(configuration);

            Assert.Equal(
                Path.Combine(directory, "override.db"),
                GetDataSource(provider));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                $"{prefix}EventArchive__DatabaseFileName",
                null);
            Environment.SetEnvironmentVariable(
                $"{prefix}EventArchive__BasePath",
                null);
            DeleteDirectory(directory);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrBlankDatabaseFileNameFails(
        string? databaseFileName)
    {
        IConfiguration configuration = ArchiveConfiguration(
            databaseFileName,
            Path.GetTempPath());

        using ServiceProvider provider =
            BuildProvider(configuration);

        OptionsValidationException exception = Assert.Throws<
            OptionsValidationException>(
                () => provider.GetRequiredService<
                    IOptions<SqliteEventArchiveOptions>>().Value);

        Assert.Contains(
            "EventArchive:DatabaseFileName is required.",
            exception.Message);
    }

    [Fact]
    public void DatabaseFileNameWithSurroundingWhitespaceFails()
    {
        AssertOptionsFailure(
            databaseFileName: " archive.db ",
            basePath: Path.GetTempPath(),
            "EventArchive:DatabaseFileName must not have surrounding whitespace.");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("\\")]
    [InlineData("directory/archive.db")]
    [InlineData("directory\\archive.db")]
    public void DatabaseFileNameWithDirectorySeparatorsFails(
        string databaseFileName)
    {
        AssertOptionsFailure(
            databaseFileName,
            Path.GetTempPath(),
            "without directory separators");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void SpecialDirectoryDatabaseFileNameFails(
        string databaseFileName)
    {
        AssertOptionsFailure(
            databaseFileName,
            Path.GetTempPath(),
            "must identify a file");
    }

    [Fact]
    public void InvalidDatabaseFileNameCharacterFails()
    {
        AssertOptionsFailure(
            "archive\0.db",
            Path.GetTempPath(),
            "invalid filename character");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankBasePathUsesApplicationDataDirectory(
        string? basePath)
    {
        string databaseFileName =
            $"archive-default-{Guid.NewGuid():N}.db";
        string expectedDirectory =
            Path.Combine(AppContext.BaseDirectory, "Data");
        string expectedPath =
            Path.Combine(expectedDirectory, databaseFileName);

        try
        {
            using ServiceProvider provider =
                BuildProvider(
                    ArchiveConfiguration(
                        databaseFileName,
                        basePath));

            Assert.Equal(expectedPath, GetDataSource(provider));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(expectedPath))
            {
                File.Delete(expectedPath);
            }
        }
    }

    [Fact]
    public void AbsoluteBasePathInitializesIntendedDatabase()
    {
        string directory = CreateTempDirectory();

        try
        {
            using ServiceProvider provider =
                BuildProvider(
                    ArchiveConfiguration(
                        "absolute.sqlite",
                        directory));

            Assert.Equal(
                Path.Combine(directory, "absolute.sqlite"),
                GetDataSource(provider));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void RelativeBasePathResolvesAgainstApplicationDirectory()
    {
        string relativeDirectory =
            $"archive-relative-{Guid.NewGuid():N}";
        string expectedDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                relativeDirectory);

        try
        {
            using ServiceProvider provider =
                BuildProvider(
                    ArchiveConfiguration(
                        "relative.db",
                        relativeDirectory));

            Assert.Equal(
                Path.Combine(expectedDirectory, "relative.db"),
                GetDataSource(provider));
        }
        finally
        {
            DeleteDirectory(expectedDirectory);
        }
    }

    [Fact]
    public void BasePathWithSurroundingWhitespaceFails()
    {
        AssertOptionsFailure(
            "archive.db",
            " data ",
            "EventArchive:BasePath must not have surrounding whitespace.");
    }

    [Fact]
    public void BasePathPointingToExistingFileFails()
    {
        string directory = CreateTempDirectory();
        string filePath = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(filePath, "test");

        try
        {
            AssertOptionsFailure(
                "archive.db",
                filePath,
                "EventArchive:BasePath points to an existing file.");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void DatabaseFileNamePointingToExistingDirectoryFails()
    {
        string directory = CreateTempDirectory();
        Directory.CreateDirectory(
            Path.Combine(directory, "archive.db"));

        try
        {
            AssertOptionsFailure(
                "archive.db",
                directory,
                "EventArchive:DatabaseFileName points to an existing directory.");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task DirectoryCreationFailureIsStartupFatal()
    {
        string directory = CreateTempDirectory();
        string blockingFile = Path.Combine(directory, "blocking-file");
        File.WriteAllText(blockingFile, "test");
        string impossibleDirectory = Path.Combine(blockingFile, "child");

        try
        {
            await using ServiceProvider provider =
                BuildProvider(
                    ArchiveConfiguration(
                        "archive.db",
                        impossibleDirectory));

            Assert.ThrowsAny<Exception>(
                () => provider.GetServices<IHostedService>().ToArray());

            Assert.False(
                File.Exists(
                    Path.Combine(
                        impossibleDirectory,
                        "archive.db")));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task OptionsValidationRunsWhenHostStarts()
    {
        IConfiguration configuration = ArchiveConfiguration(
            "..",
            Path.GetTempPath());

        using IHost host =
            new HostBuilder()
                .ConfigureServices(
                    services =>
                    {
                        services.AddLogging();
                        services.AddMissionControlArchive(
                            configuration);
                    })
                .Build();

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => host.StartAsync());

        AssertExceptionContains(
            exception,
            "EventArchive:DatabaseFileName must identify a file.");
    }

    [Fact]
    public void ValidatedOptionsInitializeTheRegisteredConnection()
    {
        string directory = CreateTempDirectory();

        try
        {
            using ServiceProvider provider =
                BuildProvider(
                    ArchiveConfiguration(
                        "validated.sqlite3",
                        directory));

            SqliteEventArchiveOptions options =
                provider.GetRequiredService<
                    IOptions<SqliteEventArchiveOptions>>().Value;
            string dataSource = GetDataSource(provider);
            string databaseFileName =
                Assert.IsType<string>(options.DatabaseFileName);

            Assert.Equal("validated.sqlite3", databaseFileName);
            Assert.Equal(directory, options.BasePath);
            Assert.Equal(
                Path.Combine(directory, databaseFileName),
                dataSource);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ConnectionInitializationCreatesExistingArchiveSchema()
    {
        string directory = CreateTempDirectory();

        try
        {
            using ServiceProvider provider =
                BuildProvider(
                    ArchiveConfiguration(
                        "schema.db",
                        directory));
            SqliteEventArchiveConnection archiveConnection =
                provider.GetRequiredService<
                    SqliteEventArchiveConnection>();

            await using var connection =
                new SqliteConnection(
                    archiveConnection.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'IntegrationEvents';
                """;

            long count = (long)(await command.ExecuteScalarAsync())!;

            Assert.Equal(1, count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task SqliteHealthCheckUsesInitializedConnectionWithoutSecondDatabase()
    {
        string directory = CreateTempDirectory();

        try
        {
            using ServiceProvider provider =
                BuildProvider(
                    ArchiveConfiguration(
                        "health.db",
                        directory));
            HealthCheckService healthChecks =
                provider.GetRequiredService<HealthCheckService>();

            HealthReport report = await healthChecks.CheckHealthAsync(
                registration => registration.Name == "sqlite");

            Assert.Equal(HealthStatus.Healthy, report.Status);
            Assert.Single(
                Directory.GetFiles(directory, "*.db"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ArchiveHealthChecksAndNatsConsumerAreRegistered()
    {
        string directory = CreateTempDirectory();

        try
        {
            IConfiguration configuration = ArchiveConfiguration(
                "registrations.db",
                directory);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMissionControlArchive(configuration);

            Assert.Contains(
                services,
                descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(NatsEventConsumer));

            using ServiceProvider provider =
                services.BuildServiceProvider();

            HealthCheckServiceOptions options = provider
                .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
                .Value;

            Assert.Contains(
                options.Registrations,
                registration => registration.Name == "sqlite");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static IConfiguration ArchiveConfiguration(
        string? databaseFileName,
        string? basePath)
    {
        var values = new Dictionary<string, string?>
        {
            ["EventArchive:DatabaseFileName"] = databaseFileName,
            ["EventArchive:BasePath"] = basePath,

            ["Nats:Url"] = "nats://localhost:4222",
            ["Nats:ClientName"] = "mission-control-archive-tests",
            ["Nats:StreamName"] = "MISSION_CONTROL_EVENTS",

            ["NatsConsumer:DurableName"] = "mission-control-archive-tests",
            ["NatsConsumer:FilterSubject"] = "events.>",
            ["NatsConsumer:MaxDeliveries"] = "2"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IConfiguration ConfigurationFromJson(string json)
    {
        return new ConfigurationBuilder()
            .AddJsonStream(
                new MemoryStream(
                    Encoding.UTF8.GetBytes(json)))
            .Build();
    }

    private static ServiceProvider BuildProvider(
        IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMissionControlArchive(configuration);

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }

    private static string GetDataSource(
        IServiceProvider provider)
    {
        SqliteEventArchiveConnection connection =
            provider.GetRequiredService<
                SqliteEventArchiveConnection>();

        return Path.GetFullPath(
            new SqliteConnectionStringBuilder(
                connection.ConnectionString).DataSource);
    }

    private static void AssertOptionsFailure(
        string? databaseFileName,
        string? basePath,
        string expectedMessage)
    {
        using ServiceProvider provider =
            BuildProvider(
                ArchiveConfiguration(
                    databaseFileName,
                    basePath));

        OptionsValidationException exception = Assert.Throws<
            OptionsValidationException>(
                () => provider.GetRequiredService<
                    IOptions<SqliteEventArchiveOptions>>().Value);

        Assert.Contains(expectedMessage, exception.Message);
    }

    private static void AssertExceptionContains(
        Exception exception,
        string expectedMessage)
    {
        for (Exception? current = exception;
            current is not null;
            current = current.InnerException)
        {
            if (current.Message.Contains(
                    expectedMessage,
                    StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail(
            $"Expected exception chain to contain '{expectedMessage}'.");
    }

    private static string CreateTempDirectory()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                $"missioncontrol-archive-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
