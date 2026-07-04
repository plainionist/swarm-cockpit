using System.Text.Json;
using Microsoft.Data.Sqlite;
using SwarmCockpit.Contracts;

namespace SwarmCockpit.Service;

public sealed class QuestionRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public QuestionRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetValue<string>("Persistence:ConnectionString")
            ?? "Data Source=./data/swarm-cockpit.db";

        InitializeDatabase();
    }

    public async Task<QuestionViewModel> CreateQuestionAsync(CreateQuestionRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var createdAt = DateTimeOffset.UtcNow;
            var optionsJson = JsonSerializer.Serialize(request.Options);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO questions (id, asking_agent, context, question_text, options_json, recommendation, status, answer, created_at, answered_at)
                VALUES ($id, $askingAgent, $context, $questionText, $optionsJson, $recommendation, 'open', NULL, $createdAt, NULL);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$askingAgent", request.AskingAgent.Trim());
            command.Parameters.AddWithValue("$context", request.Context.Trim());
            command.Parameters.AddWithValue("$questionText", request.Question.Trim());
            command.Parameters.AddWithValue("$optionsJson", optionsJson);
            command.Parameters.AddWithValue("$recommendation", request.Recommendation.Trim());
            command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken);

            return new QuestionViewModel(
                id,
                request.AskingAgent.Trim(),
                request.Context.Trim(),
                request.Question.Trim(),
                request.Options,
                request.Recommendation.Trim(),
                "open",
                null,
                createdAt,
                null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<QuestionViewModel>> GetQuestionsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, asking_agent, context, question_text, options_json, recommendation, status, answer, created_at, answered_at
                FROM questions
                ORDER BY
                    CASE status WHEN 'open' THEN 0 ELSE 1 END,
                    created_at DESC;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var items = new List<QuestionViewModel>();
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapRow(reader));
            }

            return items;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<QuestionViewModel?> GetQuestionAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, asking_agent, context, question_text, options_json, recommendation, status, answer, created_at, answered_at
                FROM questions
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", id);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return MapRow(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> AnswerQuestionAsync(string id, string answer, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE questions
                SET status = 'answered', answer = $answer, answered_at = $answeredAt
                WHERE id = $id AND status = 'open';
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$answer", answer.Trim());
            command.Parameters.AddWithValue("$answeredAt", DateTimeOffset.UtcNow.ToString("O"));

            var updated = await command.ExecuteNonQueryAsync(cancellationToken);
            return updated > 0;
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
            CREATE TABLE IF NOT EXISTS questions (
                id TEXT PRIMARY KEY,
                asking_agent TEXT NOT NULL,
                context TEXT NOT NULL,
                question_text TEXT NOT NULL,
                options_json TEXT NOT NULL,
                recommendation TEXT NOT NULL,
                status TEXT NOT NULL,
                answer TEXT NULL,
                created_at TEXT NOT NULL,
                answered_at TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static QuestionViewModel MapRow(SqliteDataReader reader)
    {
        var optionsJson = reader.GetString(4);
        var options = JsonSerializer.Deserialize<List<string>>(optionsJson) ?? new List<string>();
        var answer = reader.IsDBNull(7) ? null : reader.GetString(7);
        DateTimeOffset? answeredAt = reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9));

        return new QuestionViewModel(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            options,
            reader.GetString(5),
            reader.GetString(6),
            answer,
            DateTimeOffset.Parse(reader.GetString(8)),
            answeredAt);
    }
}
