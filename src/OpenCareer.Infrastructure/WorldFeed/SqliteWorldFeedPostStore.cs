using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.WorldFeed;
using OpenCareer.Domain.Events;

namespace OpenCareer.Infrastructure.WorldFeed;

public sealed class SqliteWorldFeedPostStore : IWorldFeedPostStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;

    public SqliteWorldFeedPostStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task SaveAsync(
        IReadOnlyCollection<WorldFeedPost> posts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(posts);
        foreach (var post in posts)
            post.Validate();

        if (posts.Count == 0)
            return;

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        foreach (var post in posts)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO WorldFeedPosts (
                    PostId, CreatedAtUnixMs, Category, Headline, Body, ScopeId, ExpiresAtUnixMs,
                    RelatedSignalIdsJson, RelatedWorldEventIdsJson, IsAiGenerated, SourceDisclosure)
                VALUES (
                    $postId, $createdAt, $category, $headline, $body, $scopeId, $expiresAt,
                    $signalIds, $eventIds, $isAi, $disclosure)
                ON CONFLICT(PostId) DO UPDATE SET
                    CreatedAtUnixMs = excluded.CreatedAtUnixMs,
                    Category = excluded.Category,
                    Headline = excluded.Headline,
                    Body = excluded.Body,
                    ScopeId = excluded.ScopeId,
                    ExpiresAtUnixMs = excluded.ExpiresAtUnixMs,
                    RelatedSignalIdsJson = excluded.RelatedSignalIdsJson,
                    RelatedWorldEventIdsJson = excluded.RelatedWorldEventIdsJson,
                    IsAiGenerated = excluded.IsAiGenerated,
                    SourceDisclosure = excluded.SourceDisclosure;
                """;

            command.Parameters.AddWithValue("$postId", post.PostId.ToString("D"));
            command.Parameters.AddWithValue("$createdAt", post.CreatedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$category", (int)post.Category);
            command.Parameters.AddWithValue("$headline", post.Headline);
            command.Parameters.AddWithValue("$body", post.Body);
            command.Parameters.AddWithValue("$scopeId", (object?)post.ScopeId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$expiresAt",
                post.ExpiresAt is { } expiry
                    ? expiry.ToUnixTimeMilliseconds()
                    : DBNull.Value);
            command.Parameters.AddWithValue(
                "$signalIds",
                JsonSerializer.Serialize(post.RelatedSignalIds ?? Array.Empty<Guid>(), JsonOptions));
            command.Parameters.AddWithValue(
                "$eventIds",
                JsonSerializer.Serialize(post.RelatedWorldEventIds ?? Array.Empty<string>(), JsonOptions));
            command.Parameters.AddWithValue("$isAi", post.IsAiGenerated ? 1 : 0);
            command.Parameters.AddWithValue(
                "$disclosure",
                (object?)post.SourceDisclosure ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<WorldFeedPost>> ReadTimelineAsync(
        DateTimeOffset asOf,
        string? scopeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                PostId, CreatedAtUnixMs, Category, Headline, Body, ScopeId, ExpiresAtUnixMs,
                RelatedSignalIdsJson, RelatedWorldEventIdsJson, IsAiGenerated, SourceDisclosure
            FROM WorldFeedPosts
            WHERE CreatedAtUnixMs <= $asOf
              AND (ExpiresAtUnixMs IS NULL OR ExpiresAtUnixMs > $asOf)
              AND ($scopeId IS NULL OR ScopeId IS NULL OR ScopeId = $scopeId)
            ORDER BY CreatedAtUnixMs DESC, PostId ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$asOf", asOf.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$scopeId", (object?)scopeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var posts = new List<WorldFeedPost>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            posts.Add(ReadPost(reader));

        return posts;
    }

    public async Task<DateTimeOffset?> GetLatestCreatedAtAsync(
        string? scopeId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT MAX(CreatedAtUnixMs)
            FROM WorldFeedPosts
            WHERE $scopeId IS NULL OR ScopeId IS NULL OR ScopeId = $scopeId;
            """;
        command.Parameters.AddWithValue("$scopeId", (object?)scopeId ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull)
            return null;

        return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    public async Task<int> DeleteExpiredAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM WorldFeedPosts
            WHERE ExpiresAtUnixMs IS NOT NULL
              AND ExpiresAtUnixMs <= $asOf;
            """;
        command.Parameters.AddWithValue("$asOf", asOf.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS WorldFeedPosts (
                    PostId TEXT NOT NULL PRIMARY KEY,
                    CreatedAtUnixMs INTEGER NOT NULL,
                    Category INTEGER NOT NULL,
                    Headline TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    ScopeId TEXT NULL,
                    ExpiresAtUnixMs INTEGER NULL,
                    RelatedSignalIdsJson TEXT NOT NULL,
                    RelatedWorldEventIdsJson TEXT NOT NULL,
                    IsAiGenerated INTEGER NOT NULL,
                    SourceDisclosure TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_WorldFeedPosts_Scope_Created
                    ON WorldFeedPosts (ScopeId, CreatedAtUnixMs DESC);

                CREATE INDEX IF NOT EXISTS IX_WorldFeedPosts_Expiry
                    ON WorldFeedPosts (ExpiresAtUnixMs);
                """;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static WorldFeedPost ReadPost(SqliteDataReader reader)
    {
        var signalIds = JsonSerializer.Deserialize<Guid[]>(
            reader.GetString(7),
            JsonOptions) ?? Array.Empty<Guid>();
        var eventIds = JsonSerializer.Deserialize<string[]>(
            reader.GetString(8),
            JsonOptions) ?? Array.Empty<string>();

        var post = new WorldFeedPost(
            Guid.Parse(reader.GetString(0)),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
            (WorldFeedPostCategory)reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6)
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
            signalIds,
            eventIds,
            reader.GetInt32(9) != 0,
            reader.IsDBNull(10) ? null : reader.GetString(10));

        post.Validate();
        return post;
    }
}
