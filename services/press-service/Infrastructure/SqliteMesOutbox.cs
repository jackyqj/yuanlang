namespace Press.Service.Infrastructure;

using Microsoft.Data.Sqlite;

public sealed class SqliteMesOutbox : IMesOutbox
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteMesOutbox> _log;

    public SqliteMesOutbox(IConfiguration config, IHostEnvironment env, ILogger<SqliteMesOutbox> log)
    {
        var root = config["Data:Root"] ?? ResolveDataRoot(env.ContentRootPath);
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "db", "press.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        _log = log;
    }

    private static string ResolveDataRoot(string contentRoot)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(contentRoot, "data"),
                     Path.GetFullPath(Path.Combine(contentRoot, "..", "data")),
                     Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "data")),
                 })
        {
            if (Directory.Exists(candidate) || candidate.EndsWith($"{Path.DirectorySeparatorChar}data"))
                return candidate;
        }
        return Path.Combine(contentRoot, "data");
    }

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS mes_outbox (
              message_id TEXT PRIMARY KEY,
              idempotency_key TEXT NOT NULL UNIQUE,
              status TEXT NOT NULL,
              payload_json TEXT NOT NULL,
              created_at TEXT NOT NULL,
              last_attempt_at TEXT,
              retry_count INTEGER NOT NULL DEFAULT 0,
              last_error TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_mes_outbox_status ON mes_outbox(status, created_at);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
        _log.LogInformation("MES outbox schema ready");
    }

    public async Task<OutboxMessage> EnqueueAsync(string idempotencyKey, string payloadJson, CancellationToken ct)
    {
        var existing = await FindByIdempotencyAsync(idempotencyKey, ct);
        if (existing is not null) return existing;

        var msg = new OutboxMessage(
            Guid.NewGuid().ToString("N"),
            "pending",
            DateTimeOffset.UtcNow,
            null,
            0,
            null,
            payloadJson,
            idempotencyKey);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mes_outbox(message_id, idempotency_key, status, payload_json, created_at, retry_count)
            VALUES ($id, $key, $status, $payload, $at, 0);
            """;
        cmd.Parameters.AddWithValue("$id", msg.MessageId);
        cmd.Parameters.AddWithValue("$key", msg.IdempotencyKey);
        cmd.Parameters.AddWithValue("$status", msg.Status);
        cmd.Parameters.AddWithValue("$payload", msg.PayloadJson);
        cmd.Parameters.AddWithValue("$at", msg.CreatedAt.ToString("O"));
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
            return msg;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return (await FindByIdempotencyAsync(idempotencyKey, ct))!;
        }
    }

    public async Task<IReadOnlyList<OutboxMessage>> ListAsync(string? status, int limit, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = status is null
            ? """
              SELECT message_id, status, created_at, last_attempt_at, retry_count, last_error, payload_json, idempotency_key
              FROM mes_outbox ORDER BY created_at DESC LIMIT $limit;
              """
            : """
              SELECT message_id, status, created_at, last_attempt_at, retry_count, last_error, payload_json, idempotency_key
              FROM mes_outbox WHERE status=$status ORDER BY created_at DESC LIMIT $limit;
              """;
        if (status is not null) cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<OutboxMessage?> RetryAsync(string messageId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE mes_outbox
            SET status='pending', last_error=NULL
            WHERE message_id=$id AND status IN ('dead','pending');
            """;
        cmd.Parameters.AddWithValue("$id", messageId);
        await cmd.ExecuteNonQueryAsync(ct);
        return await FindByIdAsync(messageId, ct);
    }

    public async Task<MesSyncStatus> GetSyncStatusAsync(string adapterName, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
              SUM(CASE WHEN status='pending' OR status='sending' THEN 1 ELSE 0 END),
              SUM(CASE WHEN status='dead' THEN 1 ELSE 0 END),
              MAX(CASE WHEN status='acked' THEN created_at END)
            FROM mes_outbox;
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var pending = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
        var dead = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
        DateTimeOffset? lastAck = reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2));
        return new MesSyncStatus(adapterName, Online: true, pending, dead, lastAck);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(int batchSize, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await using var select = conn.CreateCommand();
        select.Transaction = tx;
        select.CommandText = """
            SELECT message_id FROM mes_outbox
            WHERE status='pending'
            ORDER BY created_at ASC
            LIMIT $limit;
            """;
        select.Parameters.AddWithValue("$limit", batchSize);
        var ids = new List<string>();
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                ids.Add(reader.GetString(0));
        }

        if (ids.Count == 0)
        {
            await tx.CommitAsync(ct);
            return [];
        }

        foreach (var id in ids)
        {
            await using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE mes_outbox
                SET status='sending', last_attempt_at=$at, retry_count=retry_count+1
                WHERE message_id=$id;
                """;
            upd.Parameters.AddWithValue("$id", id);
            upd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            await upd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        var claimed = new List<OutboxMessage>();
        foreach (var id in ids)
        {
            var msg = await FindByIdAsync(id, ct);
            if (msg is not null) claimed.Add(msg);
        }
        return claimed;
    }

    public async Task MarkAckedAsync(string messageId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE mes_outbox SET status='acked', last_error=NULL, last_attempt_at=$at WHERE message_id=$id;
            """;
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(string messageId, string error, int maxAttempts, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE mes_outbox
            SET status=CASE WHEN retry_count >= $max THEN 'dead' ELSE 'pending' END,
                last_error=$err,
                last_attempt_at=$at
            WHERE message_id=$id;
            """;
        cmd.Parameters.AddWithValue("$id", messageId);
        cmd.Parameters.AddWithValue("$err", error.Length > 500 ? error[..500] : error);
        cmd.Parameters.AddWithValue("$max", maxAttempts);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<OutboxMessage?> FindByIdempotencyAsync(string key, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT message_id, status, created_at, last_attempt_at, retry_count, last_error, payload_json, idempotency_key
            FROM mes_outbox WHERE idempotency_key=$key LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        var rows = await ReadAllAsync(cmd, ct);
        return rows.FirstOrDefault();
    }

    private async Task<OutboxMessage?> FindByIdAsync(string id, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT message_id, status, created_at, last_attempt_at, retry_count, last_error, payload_json, idempotency_key
            FROM mes_outbox WHERE message_id=$id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await ReadAllAsync(cmd, ct);
        return rows.FirstOrDefault();
    }

    private static async Task<List<OutboxMessage>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<OutboxMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OutboxMessage(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }
        return list;
    }
}
