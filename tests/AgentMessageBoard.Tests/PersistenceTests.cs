using AgentMessageBoard.Server.Data;
using AgentMessageBoard.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentMessageBoard.Tests;

public sealed class PersistenceTests(MessageBoardFactory factory) : IClassFixture<MessageBoardFactory>
{
    private async Task<SqliteConnection> OpenDatabaseAsync()
    {
        _ = factory.Server; // boots the host, which migrates the database
        var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(Cancellation.Token);
        return connection;
    }

    [Fact]
    public async Task Database_file_is_created_and_uses_wal_journaling()
    {
        await using var connection = await OpenDatabaseAsync();
        Assert.True(File.Exists(factory.DatabasePath));

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        var journalMode = (string?)await command.ExecuteScalarAsync(Cancellation.Token);

        Assert.Equal("wal", journalMode, ignoreCase: true);
    }

    [Fact]
    public async Task Messages_table_exists_with_the_expected_columns()
    {
        await using var connection = await OpenDatabaseAsync();

        var tables = await ReadStringsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");
        var columns = await ReadStringsAsync(connection, "SELECT name FROM pragma_table_info('messages') ORDER BY cid;");
        var indexes = await ReadStringsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'messages' ORDER BY name;");

        Assert.Contains("messages", tables);
        Assert.Contains("__EFMigrationsHistory", tables);
        Assert.Equal(["Id", "Label", "Content", "UserAgent", "PostedById", "PostedBy", "CreatedAtUtc"], columns);
        Assert.Contains("IX_messages_CreatedAtUtc", indexes);
        Assert.Contains("IX_messages_Label_CreatedAtUtc", indexes);
    }

    [Fact]
    public async Task Initial_create_migration_is_recorded_and_nothing_is_pending()
    {
        await using var connection = await OpenDatabaseAsync();
        var applied = await ReadStringsAsync(connection, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessageBoardDbContext>();
        var known = db.Database.GetMigrations().Order().ToArray();
        var pending = await db.Database.GetPendingMigrationsAsync(Cancellation.Token);

        Assert.Contains(applied, id => id.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Equal(known, applied);
        Assert.Empty(pending);
    }

    [Fact]
    public void Connection_string_comes_from_configuration()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessageBoardDbContext>();

        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;

        Assert.Equal(factory.DatabasePath, dataSource);
    }

    [Fact]
    public async Task Each_factory_gets_its_own_database_which_is_deleted_on_dispose()
    {
        var other = new MessageBoardFactory();
        string path;
        await using (other)
        {
            _ = other.Server;
            path = other.DatabasePath;
            Assert.NotEqual(factory.DatabasePath, path);
            Assert.True(File.Exists(path));
        }

        Assert.False(File.Exists(path), "The temp database must be removed when the factory is disposed.");
        Assert.False(File.Exists(path + "-wal"));
        Assert.False(File.Exists(path + "-shm"));
    }

    private static async Task<string[]> ReadStringsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(Cancellation.Token);
        var values = new List<string>();
        while (await reader.ReadAsync(Cancellation.Token))
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }
}
