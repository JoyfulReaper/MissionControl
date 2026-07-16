extern alias AgentApp;

using JoyfulReaperLib.Sqlite;
using Microsoft.Data.Sqlite;
using AgentApp::MissionControl.Agent.Storage;

namespace MissionControl.Tests;

internal sealed class AgentSnapshotStoreFixture : IAsyncDisposable
{
    private const string DatabaseFileName = "agent-storage.db";
    private readonly string tempDirectory;

    private AgentSnapshotStoreFixture(
        string tempDirectory,
        AgentDatabase database,
        SqliteNodeSnapshotStore snapshotStore)
    {
        this.tempDirectory = tempDirectory;
        Database = database;
        SnapshotStore = snapshotStore;
    }

    public AgentDatabase Database { get; }

    public SqliteNodeSnapshotStore SnapshotStore { get; }

    public static Task<AgentSnapshotStoreFixture> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                $"missioncontrol-agent-tests-{Guid.NewGuid():N}");

        Directory.CreateDirectory(tempDirectory);

        string connectionString =
            SqliteDatabaseInitializer.Initialize(
                dbFileName: DatabaseFileName,
                schemaSql: AgentStorageSchema.Sql,
                basePath: tempDirectory);

        var database = new AgentDatabase(connectionString);
        var snapshotStore = new SqliteNodeSnapshotStore(database);

        return Task.FromResult(
            new AgentSnapshotStoreFixture(
                tempDirectory,
                database,
                snapshotStore));
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                await Task.Delay(50);
            }
        }
    }
}
