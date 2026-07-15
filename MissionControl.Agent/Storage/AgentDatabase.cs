using Microsoft.Data.Sqlite;

namespace MissionControl.Agent.Storage;

internal sealed class AgentDatabase
{
    public AgentDatabase(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            connectionString);

        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(ConnectionString);
    }
}