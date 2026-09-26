using Microsoft.Data.Sqlite;

namespace MessengerServer.Data;

public sealed class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(IHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(dataDirectory);

        var databasePath = Path.Combine(dataDirectory, "messenger.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public string ConnectionString => _connectionString;

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS messages (
                seq       INTEGER PRIMARY KEY AUTOINCREMENT,
                id        TEXT NOT NULL UNIQUE,
                type      INTEGER NOT NULL,
                sender    TEXT NOT NULL,
                body      TEXT,
                file_id   TEXT,
                sent_at   TEXT NOT NULL,
                read_at   TEXT
            );

            CREATE TABLE IF NOT EXISTS files (
                id            TEXT PRIMARY KEY,
                message_id    TEXT NOT NULL REFERENCES messages(id),
                original_name TEXT NOT NULL,
                mime_type     TEXT,
                size_bytes    INTEGER NOT NULL,
                category      TEXT NOT NULL,
                stored_path   TEXT NOT NULL,
                created_at    TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_messages_seq
                ON messages(seq DESC);
            CREATE INDEX IF NOT EXISTS idx_files_category
                ON files(category, created_at DESC);

            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                id UNINDEXED,
                body,
                tokenize = 'trigram'
            );

            CREATE TRIGGER IF NOT EXISTS messages_ai
            AFTER INSERT ON messages
            WHEN NEW.body IS NOT NULL
            BEGIN
                INSERT INTO messages_fts(id, body) VALUES (NEW.id, NEW.body);
            END;

            CREATE TRIGGER IF NOT EXISTS messages_au
            AFTER UPDATE OF body ON messages
            BEGIN
                DELETE FROM messages_fts WHERE id = OLD.id;
                INSERT INTO messages_fts(id, body)
                SELECT NEW.id, NEW.body WHERE NEW.body IS NOT NULL;
            END;

            CREATE TRIGGER IF NOT EXISTS messages_ad
            AFTER DELETE ON messages
            BEGIN
                DELETE FROM messages_fts WHERE id = OLD.id;
            END;
            """;
        command.ExecuteNonQuery();
    }
}
