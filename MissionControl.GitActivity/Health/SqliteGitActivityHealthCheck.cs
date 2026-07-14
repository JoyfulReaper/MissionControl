/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MissionControl.GitActivity.Storage.Sqlite;

namespace MissionControl.GitActivity.Health;

public sealed class SqliteGitActivityHealthCheck(
    GitActivityConnection database)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection =
                new SqliteConnection(database.ConnectionString);

            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";

            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Git activity SQLite database is unavailable.",
                exception);
        }
    }
}