using Npgsql;
using Pgvector;

namespace CompanyAssistant.Web.Knowledge;

internal sealed class NpgsqlIngestionStore : IIngestionStore, IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> _dataSource;
    private readonly string _role;

    public NpgsqlIngestionStore(DatabaseSettings settings)
    {
        var connectionString = settings.IngestionConnectionString;
        _role = settings.IngestionRole;
        _dataSource = new Lazy<NpgsqlDataSource>(
            () =>
            {
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("Database:IngestionConnectionString is required for ingestion.");
                return NpgsqlDataSources.CreateVectorDataSource(connectionString);
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<StoredDocument?> GetDocumentAsync(string documentId, CancellationToken ct)
    {
        await using var connection = await _dataSource.Value.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT document_id, source_path, content_sha256, content, title, owner, approver, " +
            "version, effective_date, review_date, approval_state, access_policy, deleted_at " +
            "FROM documents WHERE document_id = $1",
            connection);
        command.Parameters.AddWithValue(documentId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new StoredDocument(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7), reader.GetFieldValue<DateOnly>(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
            reader.GetString(10), reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));
    }

    public async Task UpsertAsync(IngestedDocument document, CancellationToken ct)
    {
        await using var connection = await _dataSource.Value.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetRoleAsync(connection, transaction, _role, ct);

        await using (var command = new NpgsqlCommand(
            "INSERT INTO documents (document_id, title, owner, approver, version, effective_date, " +
            "review_date, approval_state, access_policy, source_path, content, content_sha256, " +
            "created_at, updated_at, deleted_at) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, now(), now(), NULL) " +
            "ON CONFLICT (document_id) DO UPDATE SET title = EXCLUDED.title, owner = EXCLUDED.owner, " +
            "approver = EXCLUDED.approver, version = EXCLUDED.version, effective_date = EXCLUDED.effective_date, " +
            "review_date = EXCLUDED.review_date, approval_state = EXCLUDED.approval_state, " +
            "access_policy = EXCLUDED.access_policy, source_path = EXCLUDED.source_path, " +
            "content = EXCLUDED.content, content_sha256 = EXCLUDED.content_sha256, deleted_at = NULL, updated_at = now()",
            connection, transaction))
        {
            command.Parameters.AddWithValue(document.Metadata.DocumentId);
            command.Parameters.AddWithValue(document.Metadata.Title);
            command.Parameters.AddWithValue(document.Metadata.Owner);
            command.Parameters.Add(document.Metadata.Approver is null
                ? new NpgsqlParameter { Value = DBNull.Value }
                : new NpgsqlParameter { Value = document.Metadata.Approver });
            command.Parameters.AddWithValue(document.Metadata.Version);
            command.Parameters.AddWithValue(document.Metadata.EffectiveDate);
            command.Parameters.Add(document.Metadata.ReviewDate is null
                ? new NpgsqlParameter { Value = DBNull.Value }
                : new NpgsqlParameter { Value = document.Metadata.ReviewDate.Value });
            command.Parameters.AddWithValue(document.Metadata.ApprovalState);
            command.Parameters.AddWithValue(document.Metadata.AccessPolicy);
            command.Parameters.AddWithValue(document.SourcePath);
            command.Parameters.AddWithValue(document.Content);
            command.Parameters.AddWithValue(document.ContentSha256);
            await command.ExecuteNonQueryAsync(ct);
        }

        long versionId;
        await using (var command = new NpgsqlCommand(
            "INSERT INTO document_versions (document_id, version, approval_state, effective_date, content_sha256) " +
            "VALUES ($1, $2, $3, $4, $5) RETURNING version_id",
            connection, transaction))
        {
            command.Parameters.AddWithValue(document.Metadata.DocumentId);
            command.Parameters.AddWithValue(document.Metadata.Version);
            command.Parameters.AddWithValue(document.Metadata.ApprovalState);
            command.Parameters.AddWithValue(document.Metadata.EffectiveDate);
            command.Parameters.AddWithValue(document.ContentSha256);
            versionId = (long)(await command.ExecuteScalarAsync(ct))!;
        }

        await using (var deleteChunks = new NpgsqlCommand(
            "DELETE FROM document_chunks WHERE document_id = $1", connection, transaction))
        {
            deleteChunks.Parameters.AddWithValue(document.Metadata.DocumentId);
            await deleteChunks.ExecuteNonQueryAsync(ct);
        }

        for (var i = 0; i < document.Chunks.Count; i++)
        {
            var chunk = document.Chunks[i];
            await using var insertChunk = new NpgsqlCommand(
                "INSERT INTO document_chunks (document_id, version_id, chunk_index, section_heading, content, content_sha256, embedding) " +
                "VALUES ($1, $2, $3, $4, $5, $6, $7)",
                connection, transaction);
            insertChunk.Parameters.AddWithValue(document.Metadata.DocumentId);
            insertChunk.Parameters.AddWithValue(versionId);
            insertChunk.Parameters.AddWithValue(i);
            insertChunk.Parameters.Add(chunk.SectionHeading is null
                ? new NpgsqlParameter { Value = DBNull.Value }
                : new NpgsqlParameter { Value = chunk.SectionHeading });
            insertChunk.Parameters.AddWithValue(chunk.Content);
            insertChunk.Parameters.AddWithValue(chunk.ContentSha256);
            insertChunk.Parameters.Add(new NpgsqlParameter { Value = new Vector(chunk.Embedding) });
            await insertChunk.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<int> SoftDeleteMissingAsync(IReadOnlySet<string> seenSourcePaths, CancellationToken ct)
    {
        await using var connection = await _dataSource.Value.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetRoleAsync(connection, transaction, _role, ct);

        await using var command = new NpgsqlCommand(
            "UPDATE documents SET deleted_at = now(), updated_at = now() " +
            "WHERE deleted_at IS NULL AND NOT (source_path = ANY($1))",
            connection, transaction);
        command.Parameters.AddWithValue(seenSourcePaths.ToArray());
        var affected = await command.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
        return affected;
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

