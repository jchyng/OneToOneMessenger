using Microsoft.Data.Sqlite;
using Shared;
using MessengerServer.Data;

namespace MessengerServer.Services;

public sealed class MessageStore
{
    private readonly string _connectionString;

    public MessageStore(DatabaseInitializer database)
    {
        _connectionString = database.ConnectionString;
    }

    public async Task<MessageDto> AddTextMessageAsync(
        Guid id,
        string sender,
        string body,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sender);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO messages (id, type, sender, body, sent_at)
            VALUES ($id, $type, $sender, $body, $sentAt);
            SELECT seq
            FROM messages
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$type", (int)MsgType.Text);
        command.Parameters.AddWithValue("$sender", sender);
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$sentAt", sentAt.ToUniversalTime().ToString("O"));

        var seq = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return new MessageDto(seq, id, MsgType.Text, sender, body, null, sentAt, false, null);
    }

    public async Task<MessageDto> AddFileMessageAsync(
        Guid messageId,
        StoredFile file,
        string sender,
        string category,
        string? body,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sender);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        var type = category.Equals("image", StringComparison.OrdinalIgnoreCase)
            ? MsgType.Image
            : MsgType.File;
        var sentAt = file.CreatedAt;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO messages (id, type, sender, body, file_id, sent_at)
            VALUES ($messageId, $type, $sender, $body, $fileId, $sentAt);

            INSERT INTO files (
                id, message_id, original_name, mime_type, size_bytes,
                category, stored_path, created_at)
            VALUES (
                $fileId, $messageId, $originalName, $mimeType, $sizeBytes,
                $category, $storedPath, $createdAt);

            SELECT seq FROM messages WHERE id = $messageId;
            """;
        command.Parameters.AddWithValue("$messageId", messageId.ToString());
        command.Parameters.AddWithValue("$type", (int)type);
        command.Parameters.AddWithValue("$sender", sender);
        command.Parameters.AddWithValue("$body", (object?)body ?? DBNull.Value);
        command.Parameters.AddWithValue("$fileId", file.Id.ToString());
        command.Parameters.AddWithValue("$originalName", file.OriginalName);
        command.Parameters.AddWithValue("$mimeType", file.MimeType);
        command.Parameters.AddWithValue("$sizeBytes", file.SizeBytes);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$storedPath", file.StoredPath);
        command.Parameters.AddWithValue("$createdAt", sentAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$sentAt", sentAt.ToUniversalTime().ToString("O"));

        var seq = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        var fileDto = new FileDto(
            file.Id,
            file.OriginalName,
            file.MimeType,
            file.SizeBytes,
            category,
            file.CreatedAt);
        return new MessageDto(seq, messageId, type, sender, body, fileDto, sentAt, false, null);
    }

    public async Task<IReadOnlyList<MessageDto>> GetRecentAsync(
        int limit = 100,
        long? beforeSeq = null,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = beforeSeq.HasValue
            ? """
               SELECT m.seq, m.id, m.type, m.sender, m.body, m.sent_at, m.read_at,
                      f.id, f.original_name, f.mime_type, f.size_bytes, f.category, f.created_at
               FROM messages AS m
               LEFT JOIN files AS f ON f.id = m.file_id
               WHERE m.seq < $beforeSeq
               ORDER BY m.seq DESC
              LIMIT $limit;
              """
            : """
               SELECT m.seq, m.id, m.type, m.sender, m.body, m.sent_at, m.read_at,
                      f.id, f.original_name, f.mime_type, f.size_bytes, f.category, f.created_at
               FROM messages AS m
               LEFT JOIN files AS f ON f.id = m.file_id
               ORDER BY m.seq DESC
              LIMIT $limit;
              """;
        command.Parameters.AddWithValue("$limit", limit);
        if (beforeSeq.HasValue)
        {
            command.Parameters.AddWithValue("$beforeSeq", beforeSeq.Value);
        }

        var messages = new List<MessageDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = Guid.Parse(reader.GetString(1));
            var type = (MsgType)reader.GetInt32(2);
            var sentAt = DateTimeOffset.Parse(reader.GetString(5));
            var readAt = reader.IsDBNull(6)
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(reader.GetString(6));

            messages.Add(new MessageDto(
                reader.GetInt64(0),
                id,
                type,
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(7)
                    ? null
                    : new FileDto(
                        Guid.Parse(reader.GetString(7)),
                        reader.GetString(8),
                        reader.IsDBNull(9) ? "application/octet-stream" : reader.GetString(9),
                        reader.GetInt64(10),
                        reader.GetString(11),
                        DateTimeOffset.Parse(reader.GetString(12))),
                sentAt,
                readAt.HasValue,
                readAt));
        }

        messages.Reverse();
        return messages;
    }

    public async Task<MessageDto?> GetMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.seq, m.id, m.type, m.sender, m.body, m.sent_at, m.read_at,
                   f.id, f.original_name, f.mime_type, f.size_bytes, f.category, f.created_at
            FROM messages AS m
            LEFT JOIN files AS f ON f.id = m.file_id
            WHERE m.id = $messageId;
            """;
        command.Parameters.AddWithValue("$messageId", messageId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var readAt = reader.IsDBNull(6)
            ? (DateTimeOffset?)null
            : DateTimeOffset.Parse(reader.GetString(6));
        return new MessageDto(
            reader.GetInt64(0),
            Guid.Parse(reader.GetString(1)),
            (MsgType)reader.GetInt32(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(7)
                ? null
                : new FileDto(
                    Guid.Parse(reader.GetString(7)),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? "application/octet-stream" : reader.GetString(9),
                    reader.GetInt64(10),
                    reader.GetString(11),
                    DateTimeOffset.Parse(reader.GetString(12))),
            DateTimeOffset.Parse(reader.GetString(5)),
            readAt.HasValue,
            readAt);
    }

    public async Task<IReadOnlyList<long>> MarkReadAsync(
        IEnumerable<long> seqList,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        var sequences = seqList.Distinct().Where(seq => seq > 0).ToArray();
        if (sequences.Length == 0)
        {
            return [];
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var updated = new List<long>();
        foreach (var seq in sequences)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE messages
                SET read_at = $readAt
                WHERE seq = $seq AND read_at IS NULL
                """;
            command.Parameters.AddWithValue("$readAt", readAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$seq", seq);

            if (await command.ExecuteNonQueryAsync(cancellationToken) > 0)
            {
                updated.Add(seq);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyList<SearchResultDto>> SearchAsync(
        string query,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.seq, m.id, m.type, m.sender, m.body, m.sent_at, m.read_at,
                   snippet(messages_fts, 1, '<mark>', '</mark>', '…', 24),
                   f.id, f.original_name, f.mime_type, f.size_bytes, f.category, f.created_at
            FROM messages_fts
            INNER JOIN messages AS m ON m.id = messages_fts.id
            LEFT JOIN files AS f ON f.id = m.file_id
            WHERE messages_fts MATCH $query
            ORDER BY m.seq DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<SearchResultDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var readAt = reader.IsDBNull(6)
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(reader.GetString(6));
            var message = new MessageDto(
                reader.GetInt64(0),
                Guid.Parse(reader.GetString(1)),
                (MsgType)reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(8)
                    ? null
                    : new FileDto(
                        Guid.Parse(reader.GetString(8)),
                        reader.GetString(9),
                        reader.IsDBNull(10) ? "application/octet-stream" : reader.GetString(10),
                        reader.GetInt64(11),
                        reader.GetString(12),
                        DateTimeOffset.Parse(reader.GetString(13))),
                DateTimeOffset.Parse(reader.GetString(5)),
                readAt.HasValue,
                readAt);

            results.Add(new SearchResultDto(message, reader.GetString(7)));
        }

        return results;
    }

    public async Task<StoredFileRecord?> GetFileAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT original_name, mime_type, size_bytes, stored_path, created_at
            FROM files
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", fileId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredFileRecord(
            fileId,
            reader.GetString(0),
            reader.IsDBNull(1) ? "application/octet-stream" : reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)));
    }

    public async Task<IReadOnlyList<VaultFileDto>> GetVaultAsync(
        string? category,
        string? query,
        string sort,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToLowerInvariant();
        if (normalizedCategory is not null and not ("image" or "video" or "doc" or "etc"))
        {
            throw new ArgumentException("category must be image, video, doc, or etc.", nameof(category));
        }

        var orderBy = sort.ToLowerInvariant() switch
        {
            "oldest" => "f.created_at ASC",
            "name" => "f.original_name COLLATE NOCASE ASC, f.created_at DESC",
            _ => "f.created_at DESC"
        };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT f.id, f.message_id, m.seq, f.original_name, f.mime_type,
                   f.size_bytes, f.category, f.created_at
            FROM files AS f
            INNER JOIN messages AS m ON m.id = f.message_id
            WHERE ($category IS NULL OR f.category = $category)
              AND ($query IS NULL OR f.original_name LIKE '%' || $query || '%' COLLATE NOCASE)
            ORDER BY {orderBy}
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$category", (object?)normalizedCategory ?? DBNull.Value);
        command.Parameters.AddWithValue("$query", (object?)(string.IsNullOrWhiteSpace(query) ? null : query.Trim()) ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        var files = new List<VaultFileDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new VaultFileDto(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? "application/octet-stream" : reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7))));
        }

        return files;
    }

    public async Task<VaultSummaryDto> GetVaultSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN category = 'image' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN category = 'video' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN category = 'doc' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN category = 'etc' THEN 1 ELSE 0 END), 0)
            FROM files;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new VaultSummaryDto(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3));
    }
}

public sealed record StoredFileRecord(
    Guid Id,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    string StoredPath,
    DateTimeOffset CreatedAt);
