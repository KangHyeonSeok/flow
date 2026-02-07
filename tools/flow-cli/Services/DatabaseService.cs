using Microsoft.Data.Sqlite;
using FlowCLI.Models;

namespace FlowCLI.Services;

/// <summary>
/// SQLite database service for the documents table.
/// Provides CRUD operations and text-based search.
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly PathResolver _paths;
    private SqliteConnection? _connection;

    public DatabaseService(PathResolver paths) => _paths = paths;

    private SqliteConnection GetConnection()
    {
        if (_connection != null) return _connection;

        var dbPath = _paths.RagDbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        EnsureSchema();
        return _connection;
    }

    private void EnsureSchema()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                content TEXT NOT NULL,
                canonical_tags TEXT DEFAULT '',
                commit_id TEXT DEFAULT '',
                plan_text TEXT DEFAULT '',
                result_text TEXT DEFAULT '',
                feature_name TEXT DEFAULT '',
                state_at_creation TEXT DEFAULT '',
                metadata TEXT DEFAULT '{}',
                created_at TEXT DEFAULT (datetime('now')),
                updated_at TEXT DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_documents_tags ON documents(canonical_tags);
            CREATE INDEX IF NOT EXISTS idx_documents_feature ON documents(feature_name);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Add a document record and return its ID.</summary>
    public int AddDocument(TaskRecord record)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (content, canonical_tags, commit_id, plan_text, result_text,
                                   feature_name, state_at_creation, metadata)
            VALUES (@content, @tags, @commitId, @plan, @result, @feature, @state, @metadata);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@content", record.Content);
        cmd.Parameters.AddWithValue("@tags", record.CanonicalTags);
        cmd.Parameters.AddWithValue("@commitId", record.CommitId);
        cmd.Parameters.AddWithValue("@plan", record.PlanText);
        cmd.Parameters.AddWithValue("@result", record.ResultText);
        cmd.Parameters.AddWithValue("@feature", record.FeatureName);
        cmd.Parameters.AddWithValue("@state", record.StateAtCreation);
        cmd.Parameters.AddWithValue("@metadata", record.Metadata);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Search documents by text content and/or tags.</summary>
    public List<TaskRecord> Query(string? query = null, string? tags = null, int top = 5,
                                   bool includePlan = false, bool includeResult = false)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();

        var conditions = new List<string>();

        if (!string.IsNullOrEmpty(query))
            conditions.Add("content LIKE @query");

        string[]? tagList = null;
        if (!string.IsNullOrEmpty(tags))
        {
            tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < tagList.Length; i++)
                conditions.Add($"canonical_tags LIKE @tag{i}");
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        cmd.CommandText = $"""
            SELECT id, content, canonical_tags, commit_id, feature_name, state_at_creation,
                   metadata, created_at, updated_at
                   {(includePlan ? ", plan_text" : "")}
                   {(includeResult ? ", result_text" : "")}
            FROM documents {where}
            ORDER BY created_at DESC
            LIMIT @top
            """;

        cmd.Parameters.AddWithValue("@top", top);

        if (!string.IsNullOrEmpty(query))
            cmd.Parameters.AddWithValue("@query", $"%{query}%");

        if (tagList != null)
        {
            for (int i = 0; i < tagList.Length; i++)
                cmd.Parameters.AddWithValue($"@tag{i}", $"%{tagList[i]}%");
        }

        var results = new List<TaskRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var record = new TaskRecord
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Content = reader.GetString(reader.GetOrdinal("content")),
                CanonicalTags = reader.GetString(reader.GetOrdinal("canonical_tags")),
                CommitId = reader.GetString(reader.GetOrdinal("commit_id")),
                FeatureName = reader.GetString(reader.GetOrdinal("feature_name")),
                StateAtCreation = reader.GetString(reader.GetOrdinal("state_at_creation")),
                Metadata = reader.GetString(reader.GetOrdinal("metadata")),
                CreatedAt = reader.GetString(reader.GetOrdinal("created_at")),
                UpdatedAt = reader.GetString(reader.GetOrdinal("updated_at"))
            };

            if (includePlan) record.PlanText = reader.GetString(reader.GetOrdinal("plan_text"));
            if (includeResult) record.ResultText = reader.GetString(reader.GetOrdinal("result_text"));

            results.Add(record);
        }

        return results;
    }

    public void Dispose() => _connection?.Dispose();
}
