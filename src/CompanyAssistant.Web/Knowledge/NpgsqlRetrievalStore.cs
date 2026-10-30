using Npgsql;
using Pgvector;

namespace CompanyAssistant.Web.Knowledge;

internal sealed class NpgsqlRetrievalStore : IRetrievalStore, IAsyncDisposable
{
    // Authorization filters (deleted/approved/effective/access policy) run in the WHERE
    // clause, strictly before vector ranking (ORDER BY) and the result LIMIT.
    internal const string SearchSql =
        "SELECT dc.chunk_id, dc.document_id, d.title, d.version, dc.chunk_index, dc.section_heading, dc.content, " +
        "(dc.embedding <=> $1) AS distance " +
        "FROM document_chunks dc " +
        "JOIN documents d ON d.document_id = dc.document_id " +
        "WHERE d.deleted_at IS NULL " +
        "AND d.approval_state = 'approved' " +
        "AND d.effective_date <= CURRENT_DATE " +
        "AND d.access_policy = ANY($2) " +
        "ORDER BY distance ASC " +
        "LIMIT $3";

    internal const string SourceSql =
        "SELECT d.document_id, d.title, d.version, d.access_policy, d.effective_date, d.approval_state, d.source_path, d.content " +
        "FROM documents d " +
        "WHERE d.document_id = $1 " +
        "AND d.deleted_at IS NULL " +
        "AND d.approval_state = 'approved' " +
        "AND d.effective_date <= CURRENT_DATE " +
        "AND d.access_policy = ANY($2)";

    internal const string AuthorizedChunksSql =
        "SELECT dc.chunk_id, dc.document_id, d.title, d.version, dc.chunk_index, dc.section_heading, dc.content " +
        "FROM document_chunks dc JOIN documents d ON d.document_id = dc.document_id " +
        "WHERE dc.chunk_id = ANY($1) AND d.deleted_at IS NULL " +
        "AND d.approval_state = 'approved' AND d.effective_date <= CURRENT_DATE " +
        "AND d.access_policy = ANY($2)";

    private readonly Lazy<NpgsqlDataSource> _dataSource;
    private readonly string _role;

    public NpgsqlRetrievalStore(DatabaseSettings settings)
    {
        var connectionString = settings.RuntimeConnectionString;
        _role = settings.RuntimeRole;
        _dataSource = new Lazy<NpgsqlDataSource>(
            () =>
            {
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("Database:RuntimeConnectionString is required for retrieval.");
                return NpgsqlDataSources.CreateVectorDataSource(connectionString);
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        float[] queryEmbedding,
        IReadOnlyCollection<string> allowedPolicies,
        int limit,
        CancellationToken ct)
    {
        await using var connection = await _dataSource.Value.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetRoleAsync(connection, transaction, _role, ct);

        await using var command = new NpgsqlCommand(SearchSql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter { Value = new Vector(queryEmbedding) });
        command.Parameters.AddWithValue(allowedPolicies.ToArray());
        command.Parameters.AddWithValue(limit);

        var results = new List<RetrievedChunk>();
        {
            // Close the reader before committing: an open NpgsqlDataReader keeps the
            // connection busy and CommitAsync would throw NpgsqlOperationInProgressException.
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new RetrievedChunk(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetDouble(7)));
            }
        }

        await transaction.CommitAsync(ct);
        return results;
    }

    public async Task<SourceDocument?> GetSourceDocumentAsync(
        string documentId,
        IReadOnlyCollection<string> allowedPolicies,
        CancellationToken ct)
    {
        await using var connection = await _dataSource.Value.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetRoleAsync(connection, transaction, _role, ct);

        await using var command = new NpgsqlCommand(SourceSql, connection, transaction);
        command.Parameters.AddWithValue(documentId);
        command.Parameters.AddWithValue(allowedPolicies.ToArray());

        SourceDocument? document = null;
        {
            // Close the reader before committing: an open NpgsqlDataReader keeps the
            // connection busy and CommitAsync would throw NpgsqlOperationInProgressException.
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                document = new SourceDocument(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateOnly>(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7));
            }
        }

        await transaction.CommitAsync(ct);
        return document;
    }

    public async Task<IReadOnlyList<RetrievedChunk>> GetAuthorizedChunksAsync(
        IReadOnlyCollection<long> chunkIds,
        IReadOnlyCollection<string> allowedPolicies,
        CancellationToken ct)
    {
        if (chunkIds.Count == 0) return [];
        await using var connection = await _dataSource.Value.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetRoleAsync(connection, transaction, _role, ct);
        await using var command = new NpgsqlCommand(AuthorizedChunksSql, connection, transaction);
        command.Parameters.AddWithValue(chunkIds.ToArray());
        command.Parameters.AddWithValue(allowedPolicies.ToArray());
        var results = new List<RetrievedChunk>();
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(new RetrievedChunk(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6), 0));
        }
        await transaction.CommitAsync(ct);
        return results;
    }

    private static async Task SetRoleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string role, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SET LOCAL ROLE \"{role}\"", connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_dataSource.IsValueCreated)
            await _dataSource.Value.DisposeAsync();
    }
}
