
using Microsoft.Data.Sqlite;
using MissionControl.Archive.Contracts;

namespace MissionControl.Archive.Storage.Sqlite;

public sealed class SqliteRandomSteamAnalyticsQuery(
    SqliteEventArchiveConnection database)
{
    public async Task<RandomSteamOverview> GetOverviewAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        fromUtc = fromUtc.ToUniversalTime();
        toUtc = toUtc.ToUniversalTime();

        if (fromUtc >= toUtc)
            throw new ArgumentOutOfRangeException(nameof(toUtc), "End must be after start.");

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(CASE WHEN EventType = $visit THEN 1 END),
                COUNT(DISTINCT CASE
                    WHEN EventType = $visit
                    THEN NULLIF(json_extract(PayloadJson, '$.visitorId'), '')
                END),
                COUNT(CASE WHEN EventType = $pick THEN 1 END),
                COUNT(CASE
                    WHEN EventType = $pick
                     AND json_extract(PayloadJson, '$.succeeded') = 1
                    THEN 1
                END),
                COUNT(CASE WHEN EventType = $export THEN 1 END)
            FROM IntegrationEvents
            WHERE OccurredAt >= $fromUtc
              AND OccurredAt < $toUtc
              AND EventType IN ($visit, $pick, $export);
            """;

        command.Parameters.AddWithValue("$fromUtc", fromUtc.ToString("O"));
        command.Parameters.AddWithValue("$toUtc", toUtc.ToString("O"));
        command.Parameters.AddWithValue("$visit", "randomsteam.site-visit.recorded");
        command.Parameters.AddWithValue("$pick", "randomsteam.game-pick.completed");
        command.Parameters.AddWithValue("$export", "randomsteam.library-export.completed");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Analytics query returned no result.");

        var attempts = reader.GetInt64(2);
        var successes = reader.GetInt64(3);

        return new RandomSteamOverview(
            FromUtc: fromUtc,
            ToUtc: toUtc,
            HomepageVisits: reader.GetInt64(0),
            IdentifiedVisitors: reader.GetInt64(1),
            PickerAttempts: attempts,
            SuccessfulPicks: successes,
            FailedPicks: attempts - successes,
            CompletedExports: reader.GetInt64(4));
    }
}
