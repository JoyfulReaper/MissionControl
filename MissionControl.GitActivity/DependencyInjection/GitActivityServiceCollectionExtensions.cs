/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using JoyfulReaperLib.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MissionControl.GitActivity.Processing;
using MissionControl.GitActivity.Storage;
using MissionControl.GitActivity.Storage.Sqlite;
using MissionControl.Messaging.RabbitMq;

namespace MissionControl.GitActivity.DependencyInjection;

public static class GitActivityServiceCollectionExtensions
{
    public static IServiceCollection AddGitActivity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configuredGitActivityOptions =
            configuration
                .GetRequiredSection(GitActivityOptions.SectionName)
                .Get<GitActivityOptions>()
            ?? throw new InvalidOperationException(
                "The GitActivity configuration section is invalid.");

        var gitActivityConnectionString =
            SqliteDatabaseInitializer.Initialize(
                configuredGitActivityOptions.DatabaseFileName,
                GitActivitySchema.Sql,
                configuredGitActivityOptions.BasePath);

        services
            .AddSingleton(
                new GitActivityConnection(
                    gitActivityConnectionString))
            .AddSingleton<
                IGitActivityRepository,
                SqliteGitActivityRepository>()
            .AddSingleton<
                IIntegrationEventProcessor,
                GitActivityEventProcessor>()
            .AddRabbitMqConsumerOptions(configuration)
            .AddGitActivityOptions(configuration)
            .AddRabbitMqConnectionOptions(configuration);

        return services;
    }

    private static IServiceCollection AddGitActivityOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<GitActivityOptions>()
            .Bind(configuration.GetSection(GitActivityOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.DatabaseFileName),
                "GitActivity:DatabaseFileName is required.")
            .Validate(
                options => options.DefaultResultLimit > 0,
                "GitActivity:DefaultResultLimit must be greater than zero.")
            .Validate(
                options => options.MaxResultLimit > 0,
                "GitActivity:MaxResultLimit must be greater than zero.")
            .Validate(
                options =>
                    options.DefaultResultLimit <= options.MaxResultLimit,
                "GitActivity:DefaultResultLimit must not exceed MaxResultLimit.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.ApiKey) &&
                    options.ApiKey.Length >= 32,
                "GitActivity:ApiKey must contain at least 32 characters.")
            .Validate(
                options => options.AllowedRepositories.Length > 0,
                "GitActivity:AllowedRepositories must contain at least one repository.")
            .Validate(
                options => options.AllowedBranches.Length > 0,
                "GitActivity:AllowedBranches must contain at least one branch.")
            .ValidateOnStart();

        return services;
    }
}
