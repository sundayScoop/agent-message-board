using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgentMessageBoard.Server.Data;

internal static partial class PersistenceExtensions
{
    public const string ConnectionStringName = "MessageBoard";
    private const string DefaultConnectionString = "Data Source=data/agent-board.db";
    private const string LogCategory = "AgentMessageBoard.Database";

    public static IServiceCollection AddMessageBoardPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? DefaultConnectionString;
        services.AddDbContext<MessageBoardDbContext>(options => options.UseSqlite(connectionString));
        return services;
    }

    /// <summary>Creates the database folder if needed, applies pending migrations and switches SQLite to WAL journaling.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessageBoardDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);

        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString() ?? string.Empty).DataSource;
        EnsureDirectoryExists(dataSource);

        await db.Database.MigrateAsync(cancellationToken);

        var journalMode = await SetJournalModeWalAsync(db, cancellationToken);
        if (string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            LogReady(logger, dataSource);
        }
        else
        {
            LogUnexpectedJournalMode(logger, journalMode);
        }
    }

    /// <summary>WAL lets agents write concurrently without blocking the human read endpoint. Returns the journal mode SQLite reports back.</summary>
    private static async Task<string?> SetJournalModeWalAsync(MessageBoardDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>SQLite creates the database file but not its folder. In-memory and URI data sources have no folder.</summary>
    private static void EnsureDirectoryExists(string dataSource)
    {
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:" || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    [LoggerMessage(LogLevel.Information, "SQLite ready ({DataSource}), journal_mode=wal.")]
    private static partial void LogReady(ILogger logger, string dataSource);

    [LoggerMessage(LogLevel.Warning, "SQLite journal_mode is '{JournalMode}', expected 'wal' (in-memory databases cannot use WAL).")]
    private static partial void LogUnexpectedJournalMode(ILogger logger, string? journalMode);
}
