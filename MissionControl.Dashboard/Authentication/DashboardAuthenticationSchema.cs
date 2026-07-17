namespace MissionControl.Dashboard.Authentication;

internal static class DashboardAuthenticationSchema
{
    internal const string Sql =
        """
        CREATE TABLE IF NOT EXISTS DashboardUsers
        (
            Id                  INTEGER NOT NULL
                                PRIMARY KEY AUTOINCREMENT,

            Username            TEXT NOT NULL,

            NormalizedUsername  TEXT NOT NULL
                                UNIQUE,

            DisplayName         TEXT NOT NULL,

            PasswordHash        TEXT NOT NULL,

            IsEnabled           INTEGER NOT NULL
                                DEFAULT 1,

            FailedLoginCount    INTEGER NOT NULL
                                DEFAULT 0,

            LockoutEndUtc       TEXT NULL,

            SecurityStamp       TEXT NOT NULL,

            CreatedAtUtc        TEXT NOT NULL,

            UpdatedAtUtc        TEXT NOT NULL
        );
        """;
}