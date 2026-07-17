using Dapper;
using System.Globalization;

namespace MissionControl.Dashboard.Authentication;

public sealed class SqliteDashboardUserStore(
    DashboardAuthenticationDatabase database)
    : IDashboardUserStore
{
    public async Task<DashboardUser?>
        FindByNormalizedUsernameAsync(
            string normalizedUsername,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            normalizedUsername);

        const string sql =
            """
            SELECT
                Id,
                Username,
                NormalizedUsername,
                DisplayName,
                PasswordHash,
                IsEnabled,
                FailedLoginCount,
                LockoutEndUtc,
                SecurityStamp,
                CreatedAtUtc,
                UpdatedAtUtc
            FROM DashboardUsers
            WHERE NormalizedUsername = @NormalizedUsername
            LIMIT 1;
            """;

        await using var connection =
            database.CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        DashboardUserRow? row =
            await connection
                .QuerySingleOrDefaultAsync<DashboardUserRow>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            NormalizedUsername =
                                normalizedUsername
                        },
                        cancellationToken:
                            cancellationToken));

        return row is null
            ? null
            : Map(row);
    }

    public async Task<DashboardUser> CreateAsync(
        NewDashboardUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        const string sql =
            """
                INSERT INTO DashboardUsers
                (
                    Username,
                    NormalizedUsername,
                    DisplayName,
                    PasswordHash,
                    IsEnabled,
                    FailedLoginCount,
                    LockoutEndUtc,
                    SecurityStamp,
                    CreatedAtUtc,
                    UpdatedAtUtc
                )
                VALUES
                (
                    @Username,
                    @NormalizedUsername,
                    @DisplayName,
                    @PasswordHash,
                    1,
                    0,
                    NULL,
                    @SecurityStamp,
                    @CreatedAtUtc,
                    @UpdatedAtUtc
                )
                RETURNING
                    Id,
                    Username,
                    NormalizedUsername,
                    DisplayName,
                    PasswordHash,
                    IsEnabled,
                    FailedLoginCount,
                    LockoutEndUtc,
                    SecurityStamp,
                    CreatedAtUtc,
                    UpdatedAtUtc;
                """;

        string createdAtUtc =
            FormatTimestamp(user.CreatedAtUtc);

        var parameters = new
        {
            user.Username,
            user.NormalizedUsername,
            user.DisplayName,
            user.PasswordHash,
            user.SecurityStamp,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

        await using var connection =
            database.CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        DashboardUserRow row =
            await connection.QuerySingleAsync<DashboardUserRow>(
                new CommandDefinition(
                    sql,
                    parameters,
                    cancellationToken:
                        cancellationToken));

        return Map(row);
    }

    private static DashboardUser Map(
        DashboardUserRow row)
    {
        return new DashboardUser(
            Id: row.Id,
            Username: row.Username,
            NormalizedUsername:
                row.NormalizedUsername,
            DisplayName: row.DisplayName,
            PasswordHash: row.PasswordHash,
            IsEnabled: row.IsEnabled != 0,
            FailedLoginCount:
                checked((int)row.FailedLoginCount),
            LockoutEndUtc:
                ParseOptionalTimestamp(
                    row.LockoutEndUtc),
            SecurityStamp: row.SecurityStamp,
            CreatedAtUtc:
                ParseTimestamp(row.CreatedAtUtc),
            UpdatedAtUtc:
                ParseTimestamp(row.UpdatedAtUtc));
    }

    private static DateTimeOffset ParseTimestamp(
        string value)
    {
        return DateTimeOffset.ParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static DateTimeOffset?
        ParseOptionalTimestamp(
            string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : ParseTimestamp(value);
    }

    private static string FormatTimestamp(
        DateTimeOffset value)
    {
        return value
            .ToUniversalTime()
            .ToString(
                "O",
                CultureInfo.InvariantCulture);
    }

    private sealed class DashboardUserRow
    {
        public long Id { get; init; }

        public required string Username { get; init; }

        public required string NormalizedUsername
        {
            get;
            init;
        }

        public required string DisplayName { get; init; }

        public required string PasswordHash { get; init; }

        public long IsEnabled { get; init; }

        public long FailedLoginCount { get; init; }

        public string? LockoutEndUtc { get; init; }

        public required string SecurityStamp { get; init; }

        public required string CreatedAtUtc { get; init; }

        public required string UpdatedAtUtc { get; init; }
    }
}