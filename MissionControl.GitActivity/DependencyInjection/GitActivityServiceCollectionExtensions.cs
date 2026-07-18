/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using JoyfulReaperLib.Sqlite;
using Microsoft.Extensions.Options;
using MissionControl.GitActivity.Health;
using MissionControl.GitActivity.Processing;
using MissionControl.GitActivity.Storage;
using MissionControl.GitActivity.Storage.Sqlite;
using MissionControl.Messaging.RabbitMq;
using MissionControl.Observability.RabbitMq;

namespace MissionControl.GitActivity.DependencyInjection;

public static class GitActivityServiceCollectionExtensions
{
    public static IServiceCollection AddGitActivity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection gitActivitySection =
            configuration.GetRequiredSection(
                GitActivityOptions.SectionName);

        services
            .AddOptions<GitActivityOptions>()
            .Bind(gitActivitySection)
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<GitActivityOptions>,
            GitActivityOptionsValidator>();

        services
            .AddWindowsService(options =>
                {
                    options.ServiceName =
                        "Mission Control Git Activity";
                })
            .AddSingleton(CreateGitActivityConnection)
            .AddSingleton<
                IGitActivityRepository,
                SqliteGitActivityRepository>()
            .AddSingleton<
                IIntegrationEventProcessor,
                GitActivityEventProcessor>()
            .AddRabbitMqConsumerOptions(configuration)
            .AddRabbitMqConnectionOptions(configuration)
            .AddRabbitMqEventConsumer();

        services
            .AddHealthChecks()
            .AddCheck<SqliteGitActivityHealthCheck>(
                "sqlite",
                tags: ["ready"])
            .AddCheck<RabbitMqConnectionHealthCheck>(
                "rabbitmq",
                tags: ["ready"]);

        return services;
    }

    private static GitActivityConnection CreateGitActivityConnection(
        IServiceProvider serviceProvider)
    {
        GitActivityOptions options =
            serviceProvider
                .GetRequiredService<IOptions<GitActivityOptions>>()
                .Value;

        string databasePath =
            GitActivityStoragePath.ResolveDatabasePath(options);

        string connectionString =
            SqliteDatabaseInitializer.Initialize(
                dbFileName: Path.GetFileName(databasePath),
                schemaSql: GitActivitySchema.Sql,
                basePath: Path.GetDirectoryName(databasePath));

        return new GitActivityConnection(connectionString);
    }
}
