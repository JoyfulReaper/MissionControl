using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MissionControl.Archive.Storage.Sqlite;

namespace MissionControl.Archive.Health;

public sealed class SqliteArchiveHealthCheck(
    SqliteEventArchiveConnection database)
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
                "SQLite archive is unavailable.",
                exception);
        }
    }
}