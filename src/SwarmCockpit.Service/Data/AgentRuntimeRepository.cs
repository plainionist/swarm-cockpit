using Microsoft.Data.Sqlite;
using SwarmCockpit.Contracts;

namespace SwarmCockpit.Service;

public sealed class AgentRuntimeRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AgentRuntimeRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetValue<string>("Persistence:ConnectionString")
            ?? "Data Source=./data/swarm-cockpit.db";

        InitializeDatabase();
    }

    public async Task<AgentLogLineViewModel> AppendLogAsync(
        string agentName,
        string message,
        string stream,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var createdAt = DateTimeOffset.UtcNow;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO agent_logs (agent_name, message, stream, created_at)
                VALUES ($agentName, $message, $stream, $createdAt)
                RETURNING id;
                """;
            command.Parameters.AddWithValue("$agentName", agentName.Trim());
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$stream", string.IsNullOrWhiteSpace(stream) ? "stdout" : stream.Trim());
            command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));

            var id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);

            return new AgentLogLineViewModel(
                id,
                agentName.Trim(),
                message,
                string.IsNullOrWhiteSpace(stream) ? "stdout" : stream.Trim(),
                createdAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentLogLineViewModel>> GetRecentLogsAsync(string agentName, int take, CancellationToken cancellationToken)
    {
        var normalizedTake = Math.Clamp(take, 1, 500);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, agent_name, message, stream, created_at
                FROM agent_logs
                WHERE agent_name = $agentName COLLATE NOCASE
                ORDER BY id DESC
                LIMIT $take;
                """;
            command.Parameters.AddWithValue("$agentName", agentName);
            command.Parameters.AddWithValue("$take", normalizedTake);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<AgentLogLineViewModel>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(MapRow(reader));
            }

            rows.Reverse();
            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentLogLineViewModel>> GetLogsAfterAsync(
        string agentName,
        long afterId,
        int take,
        CancellationToken cancellationToken)
    {
        var normalizedTake = Math.Clamp(take, 1, 500);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, agent_name, message, stream, created_at
                FROM agent_logs
                WHERE agent_name = $agentName COLLATE NOCASE AND id > $afterId
                ORDER BY id ASC
                LIMIT $take;
                """;
            command.Parameters.AddWithValue("$agentName", agentName);
            command.Parameters.AddWithValue("$afterId", afterId);
            command.Parameters.AddWithValue("$take", normalizedTake);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<AgentLogLineViewModel>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(MapRow(reader));
            }

            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentScreenViewModel> UpsertScreenAsync(
        string agentName,
        string content,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var capturedAt = DateTimeOffset.UtcNow;
            var trimmedName = agentName.Trim();

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Determine whether the rendered screen actually changed. changed_at
            // only advances on real content changes, so it reflects terminal
            // activity rather than the fixed capture cadence.
            var existing = connection.CreateCommand();
            existing.CommandText = "SELECT content, changed_at FROM agent_screens WHERE agent_name = $agentName;";
            existing.Parameters.AddWithValue("$agentName", trimmedName);

            string? previousContent = null;
            string? previousChangedAt = null;
            await using (var reader = await existing.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    previousContent = reader.GetString(0);
                    previousChangedAt = reader.GetString(1);
                }
            }

            var contentChanged = !string.Equals(previousContent, content, StringComparison.Ordinal);
            var changedAt = contentChanged || previousChangedAt is null
                ? capturedAt.ToString("O")
                : previousChangedAt;

            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO agent_screens (agent_name, content, captured_at, changed_at)
                VALUES ($agentName, $content, $capturedAt, $changedAt)
                ON CONFLICT(agent_name) DO UPDATE SET
                    content = excluded.content,
                    captured_at = excluded.captured_at,
                    changed_at = excluded.changed_at;
                """;
            command.Parameters.AddWithValue("$agentName", trimmedName);
            command.Parameters.AddWithValue("$content", content);
            command.Parameters.AddWithValue("$capturedAt", capturedAt.ToString("O"));
            command.Parameters.AddWithValue("$changedAt", changedAt);

            await command.ExecuteNonQueryAsync(cancellationToken);

            return new AgentScreenViewModel(trimmedName, content, capturedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentScreenViewModel?> GetScreenAsync(string agentName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT agent_name, content, captured_at
                FROM agent_screens
                WHERE agent_name = $agentName COLLATE NOCASE
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$agentName", agentName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new AgentScreenViewModel(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentInputViewModel> EnqueueInputAsync(
        string agentName,
        string text,
        bool submit,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var createdAt = DateTimeOffset.UtcNow;
            var trimmedName = agentName.Trim();
            // Answers are single-line; strip embedded newlines so send-keys stays predictable.
            var sanitized = text.Replace("\r", string.Empty).Replace("\n", string.Empty);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO agent_inputs (agent_name, text, submit, created_at)
                VALUES ($agentName, $text, $submit, $createdAt)
                RETURNING id;
                """;
            command.Parameters.AddWithValue("$agentName", trimmedName);
            command.Parameters.AddWithValue("$text", sanitized);
            command.Parameters.AddWithValue("$submit", submit ? 1 : 0);
            command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));

            var id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            return new AgentInputViewModel(id, trimmedName, sanitized, submit, createdAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentInputViewModel>> GetPendingInputsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, agent_name, text, submit, created_at
                FROM agent_inputs
                WHERE delivered_at IS NULL
                ORDER BY id ASC;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<AgentInputViewModel>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new AgentInputViewModel(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3) != 0,
                    DateTimeOffset.Parse(reader.GetString(4))));
            }

            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> MarkInputDeliveredAsync(long id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE agent_inputs
                SET delivered_at = $deliveredAt
                WHERE id = $id AND delivered_at IS NULL;
                """;
            command.Parameters.AddWithValue("$deliveredAt", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$id", id);

            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentActivitySnapshot>> GetLatestAgentActivityAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT agent_name, message, created_at
                FROM (
                    SELECT l.agent_name AS agent_name, l.message AS message, l.created_at AS created_at
                    FROM agent_logs l
                    INNER JOIN (
                        SELECT agent_name, MAX(id) AS max_id
                        FROM agent_logs
                        GROUP BY agent_name
                    ) m ON l.id = m.max_id
                    UNION ALL
                    SELECT agent_name AS agent_name, '' AS message, changed_at AS created_at
                    FROM agent_screens
                )
                ORDER BY agent_name;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var latestByAgent = new Dictionary<string, AgentActivitySnapshot>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
            {
                var snapshot = new AgentActivitySnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTimeOffset.Parse(reader.GetString(2)));

                if (!latestByAgent.TryGetValue(snapshot.AgentName, out var existing)
                    || snapshot.LastActivity > existing.LastActivity)
                {
                    latestByAgent[snapshot.AgentName] = snapshot;
                }
            }

            return latestByAgent.Values.OrderBy(s => s.AgentName, StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ClearLogsAsync(string agentName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM agent_logs
                WHERE agent_name = $agentName COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$agentName", agentName);

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void InitializeDatabase()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        if (!string.IsNullOrWhiteSpace(builder.DataSource))
        {
            var dbFilePath = Path.GetFullPath(builder.DataSource);
            var folder = Path.GetDirectoryName(dbFilePath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS agent_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                agent_name TEXT NOT NULL,
                message TEXT NOT NULL,
                stream TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_agent_logs_agent_id
            ON agent_logs(agent_name, id);

            CREATE TABLE IF NOT EXISTS agent_screens (
                agent_name TEXT PRIMARY KEY,
                content TEXT NOT NULL,
                captured_at TEXT NOT NULL,
                changed_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS agent_inputs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                agent_name TEXT NOT NULL,
                text TEXT NOT NULL,
                submit INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                delivered_at TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_agent_inputs_pending
            ON agent_inputs(delivered_at, id);
            """;
        command.ExecuteNonQuery();

        // Migration: older databases have agent_screens without changed_at.
        var migrate = connection.CreateCommand();
        migrate.CommandText =
            """
            SELECT COUNT(*) FROM pragma_table_info('agent_screens') WHERE name = 'changed_at';
            """;
        var hasChangedAt = Convert.ToInt64(migrate.ExecuteScalar()) > 0;
        if (!hasChangedAt)
        {
            var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE agent_screens ADD COLUMN changed_at TEXT NOT NULL DEFAULT '';";
            alter.ExecuteNonQuery();

            var backfill = connection.CreateCommand();
            backfill.CommandText = "UPDATE agent_screens SET changed_at = captured_at WHERE changed_at = '';";
            backfill.ExecuteNonQuery();
        }
    }

    private static AgentLogLineViewModel MapRow(SqliteDataReader reader)
    {
        return new AgentLogLineViewModel(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)));
    }
}

public sealed record AgentActivitySnapshot(string AgentName, string LastMessage, DateTimeOffset LastActivity);
