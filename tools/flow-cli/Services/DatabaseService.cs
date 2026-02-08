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
    private readonly EmbeddingBridge _embeddingBridge;
    private int? _embeddingDimension;
    private bool _vectorIndexChecked;
    private SqliteConnection? _connection;

    /// <summary>Default embedding dimension (BGE-M3 model = 1024).</summary>
    public const int DefaultEmbeddingDimension = 1024;

    public DatabaseService(PathResolver paths)
    {
        _paths = paths;
        _embeddingBridge = new EmbeddingBridge(paths);
    }

    private SqliteConnection GetConnection()
    {
        if (_connection != null) return _connection;

        var dbPath = _paths.RagDbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        LoadSqliteVecExtension();
        EnsureSchema();
        return _connection;
    }

    /// <summary>
    /// Loads the sqlite-vec extension (vec0.dll) if available.
    /// Falls back gracefully if not installed — vector search will be unavailable.
    /// </summary>
    private void LoadSqliteVecExtension()
    {
        try
        {
            var vecPath = _paths.EmbedExePath.Replace("embed.exe", "vec0");
            // Also check the rag/bin directory directly
            var ragBinDir = Path.GetDirectoryName(_paths.EmbedExePath);
            var vec0Path = ragBinDir != null ? Path.Combine(ragBinDir, "vec0") : vecPath;

            if (File.Exists(vec0Path + ".dll") || File.Exists(vec0Path + ".so") || File.Exists(vec0Path))
            {
                _connection!.EnableExtensions(true);
                _connection.LoadExtension(vec0Path);
            }
        }
        catch
        {
            // sqlite-vec not available — vector search will be disabled
            // Text-based LIKE search remains functional
        }
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

        // Vector search tables (requires sqlite-vec extension)
        EnsureVectorSchema();
    }

    /// <summary>
    /// Creates vec_documents virtual table and vector_index_meta tracking table.
    /// Attempts dimension detection from embed.exe; falls back to default (1024).
    /// Fails silently if sqlite-vec extension is not loaded.
    /// </summary>
    private void EnsureVectorSchema()
    {
        try
        {
            int dimension = DefaultEmbeddingDimension;
            try
            {
                dimension = Task.Run(() => DetectEmbeddingDimensionAsync()).GetAwaiter().GetResult();
            }
            catch
            {
                // embed.exe not available — use default dimension
            }

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = $"""
                CREATE VIRTUAL TABLE IF NOT EXISTS vec_documents USING vec0(
                    embedding float[{dimension}]
                );
                """;
            cmd.ExecuteNonQuery();

            using var cmd2 = _connection.CreateCommand();
            cmd2.CommandText = """
                CREATE TABLE IF NOT EXISTS vector_index_meta (
                    document_id INTEGER PRIMARY KEY,
                    indexed_at TEXT NOT NULL DEFAULT (datetime('now')),
                    embedding_version TEXT DEFAULT 'v1',
                    FOREIGN KEY (document_id) REFERENCES documents(id)
                );
                """;
            cmd2.ExecuteNonQuery();
        }
        catch
        {
            // sqlite-vec not loaded — vector tables won't be created
            // Text-based search remains fully functional
        }
    }

    /// <summary>
    /// Detects the embedding dimension by generating a test embedding.
    /// Caches the result for subsequent calls.
    /// </summary>
    public async Task<int> DetectEmbeddingDimensionAsync()
    {
        if (_embeddingDimension.HasValue)
            return _embeddingDimension.Value;

        var testEmbedding = await _embeddingBridge.GenerateEmbeddingAsync("test").ConfigureAwait(false);

        if (testEmbedding == null)
            throw new InvalidOperationException(
                "Failed to generate test embedding. Ensure embed.exe is built (run build-embed.ps1).");

        _embeddingDimension = testEmbedding.Length;
        return _embeddingDimension.Value;
    }

    /// <summary>
    /// Generates an embedding vector for the given text.
    /// Returns null on failure (embed.exe not found, process error, etc.).
    /// </summary>
    public async Task<float[]?> GetEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return await _embeddingBridge.GenerateEmbeddingAsync(text).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Warning: Embedding generation failed: {text[..Math.Min(50, text.Length)]}... Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Indexes all unindexed documents into vec_documents.
    /// Skips silently if vector tables don't exist (sqlite-vec not loaded).
    /// Sets _vectorIndexChecked flag to avoid repeated attempts.
    /// </summary>
    public async Task EnsureVectorIndexAsync()
    {
        if (_vectorIndexChecked) return;
        _vectorIndexChecked = true;

        var conn = GetConnection();

        // Check if vector tables exist (sqlite-vec might not be loaded)
        if (!VectorTablesExist(conn)) return;

        // Find unindexed documents
        var unindexedDocs = new List<(int id, string content)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT d.id, d.content
                FROM documents d
                LEFT JOIN vector_index_meta vim ON d.id = vim.document_id
                WHERE vim.document_id IS NULL
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                unindexedDocs.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        if (unindexedDocs.Count == 0) return;

        Console.WriteLine($"Indexing {unindexedDocs.Count} documents...");
        int indexed = 0;

        foreach (var (id, content) in unindexedDocs)
        {
            var embedding = await GetEmbeddingAsync(content).ConfigureAwait(false);
            if (embedding == null)
            {
                Console.Error.WriteLine($"Warning: Failed to generate embedding for document {id}");
                continue;
            }

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = "INSERT INTO vec_documents(rowid, embedding) VALUES (@rowid, @embedding)";
            insertCmd.Parameters.AddWithValue("@rowid", id);
            insertCmd.Parameters.AddWithValue("@embedding", SerializeVector(embedding));
            insertCmd.ExecuteNonQuery();

            using var metaCmd = conn.CreateCommand();
            metaCmd.CommandText = """
                INSERT INTO vector_index_meta(document_id, indexed_at, embedding_version)
                VALUES (@docId, datetime('now'), 'v1')
                """;
            metaCmd.Parameters.AddWithValue("@docId", id);
            metaCmd.ExecuteNonQuery();

            indexed++;
            if (indexed % 10 == 0)
                Console.WriteLine($"  Indexed {indexed}/{unindexedDocs.Count} documents...");
        }

        Console.WriteLine($"Indexed {indexed}/{unindexedDocs.Count} documents.");
    }

    /// <summary>Checks if vec_documents and vector_index_meta tables exist.</summary>
    private static bool VectorTablesExist(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table') AND name='vector_index_meta'";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return count > 0;
    }

    /// <summary>Serializes a float[] vector to a byte[] blob (little-endian float32).</summary>
    public static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Deserializes a byte[] blob back to float[] vector.</summary>
    public static float[] DeserializeVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
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

    /// <summary>Search documents by text content and/or tags with hybrid scoring.</summary>
    public List<TaskRecord> Query(string? query = null, string? tags = null, int top = 5,
                                   bool includePlan = false, bool includeResult = false)
    {
        var conn = GetConnection();

        // Auto-index on first query (no-op if vec tables don't exist or already indexed)
        try { EnsureVectorIndexAsync().GetAwaiter().GetResult(); }
        catch { /* indexing failure should not block text search */ }

        // Parse tags (OR condition, deduplicated)
        string[]? tagList = null;
        if (!string.IsNullOrEmpty(tags))
            tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToArray();

        // Try vector search first
        if (!string.IsNullOrEmpty(query))
        {
            try
            {
                var queryEmbedding = Task.Run(() => GetEmbeddingAsync(query)).GetAwaiter().GetResult();
                if (queryEmbedding != null && VectorTablesExist(conn))
                    return VectorHybridSearch(conn, queryEmbedding, tagList, top, includePlan, includeResult);
            }
            catch { /* fall through to LIKE search */ }
        }

        // Fallback: LIKE-based search with hybrid scoring
        return LikeHybridSearch(conn, query, tagList, top, includePlan, includeResult);
    }

    /// <summary>LIKE-based search with in-memory hybrid scoring (OR tags).</summary>
    private List<TaskRecord> LikeHybridSearch(SqliteConnection conn, string? query, string[]? tagList,
        int top, bool includePlan, bool includeResult)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText = $"""
            SELECT id, content, canonical_tags, commit_id, feature_name, state_at_creation,
                   metadata, created_at, updated_at
                   {(includePlan ? ", plan_text" : "")}
                   {(includeResult ? ", result_text" : "")}
            FROM documents
            ORDER BY created_at DESC
            """;

        var candidates = ReadRecords(cmd, includePlan, includeResult);

        // No filters → return all (preserves original behavior)
        bool hasQuery = !string.IsNullOrEmpty(query);
        bool hasTags = tagList is { Length: > 0 };
        if (!hasQuery && !hasTags)
            return ApplyTop(candidates, top);

        // Score each candidate
        var scored = new List<(TaskRecord record, float score)>();
        foreach (var record in candidates)
        {
            float textScore = hasQuery &&
                record.Content.Contains(query!, StringComparison.OrdinalIgnoreCase)
                ? 0.5f : 0f;

            int matchedTags = hasTags ? CountMatchedTags(record.CanonicalTags, tagList!) : 0;

            float tagScore = hasTags ? (float)matchedTags / tagList!.Length * 0.1f : 0f;
            float score = textScore + tagScore;

            if (score > 0)
                scored.Add((record, score));
        }

        return ApplyTop(scored
            .OrderByDescending(s => s.score)
            .Select(s => s.record)
            .ToList(), top);
    }

    /// <summary>Vector-based hybrid search with cosine distance + tag scoring.</summary>
    private List<TaskRecord> VectorHybridSearch(SqliteConnection conn, float[] queryEmbedding,
        string[]? tagList, int top, bool includePlan, bool includeResult)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText = $"""
            SELECT d.id, d.content, d.canonical_tags, d.commit_id, d.feature_name,
                   d.state_at_creation, d.metadata, d.created_at, d.updated_at
                   {(includePlan ? ", d.plan_text" : "")}
                   {(includeResult ? ", d.result_text" : "")},
                   vec_distance_cosine(v.embedding, @queryVec) AS distance
            FROM vec_documents v
            JOIN documents d ON v.rowid = d.id
            ORDER BY distance
            LIMIT 100
            """;
        cmd.Parameters.AddWithValue("@queryVec", SerializeVector(queryEmbedding));

        var results = new List<(TaskRecord record, float score)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var record = ReadSingleRecord(reader, includePlan, includeResult);
            float distance = reader.GetFloat(reader.FieldCount - 1);

            int matchedTags = tagList is { Length: > 0 }
                ? CountMatchedTags(record.CanonicalTags, tagList)
                : 0;

            float score = CalculateHybridScore(distance, matchedTags, tagList?.Length ?? 0);
            results.Add((record, score));
        }

        return ApplyTop(results
            .OrderByDescending(r => r.score)
            .Select(r => r.record)
            .ToList(), top);
    }

    /// <summary>
    /// Calculates hybrid search score from cosine distance and tag matches.
    /// Score = semanticScore * 0.5 + tagScore * 0.1
    /// </summary>
    public static float CalculateHybridScore(float cosineDistance, int matchedTags, int totalQueryTags)
    {
        float semanticScore = Math.Max(0, 1.0f - cosineDistance);
        float tagScore = totalQueryTags > 0 ? (float)matchedTags / totalQueryTags : 0f;
        return (semanticScore * 0.5f) + (tagScore * 0.1f);
    }

    /// <summary>
    /// Counts how many query tags match the canonical tags (OR condition, case-insensitive).
    /// Uses Contains for substring matching (e.g. "cli" matches "cli,command").
    /// </summary>
    public static int CountMatchedTags(string canonicalTags, string[] queryTags)
    {
        if (queryTags == null || queryTags.Length == 0)
            return 0;

        int count = 0;
        foreach (var tag in queryTags)
            if (canonicalTags.Contains(tag, StringComparison.OrdinalIgnoreCase))
                count++;
        return count;
    }

    private static List<TaskRecord> ReadRecords(SqliteCommand cmd, bool includePlan, bool includeResult)
    {
        var results = new List<TaskRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadSingleRecord(reader, includePlan, includeResult));
        return results;
    }

    private static TaskRecord ReadSingleRecord(SqliteDataReader reader, bool includePlan, bool includeResult)
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
        return record;
    }

    /// <summary>Applies the top (LIMIT) parameter, handling -1 as "no limit".</summary>
    private static List<TaskRecord> ApplyTop(List<TaskRecord> records, int top)
    {
        if (top < 0) return records; // LIMIT -1 = no limit (SQLite compat)
        if (top == 0) return [];
        return records.Take(top).ToList();
    }

    public void Dispose() => _connection?.Dispose();
}
