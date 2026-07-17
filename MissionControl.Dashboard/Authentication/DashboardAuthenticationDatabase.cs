using Microsoft.Data.Sqlite;

namespace MissionControl.Dashboard.Authentication;

public sealed class DashboardAuthenticationDatabase
{
    public DashboardAuthenticationDatabase(
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            connectionString);

        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(
            ConnectionString);
    }
}