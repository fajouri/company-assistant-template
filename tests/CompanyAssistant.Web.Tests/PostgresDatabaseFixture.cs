using CompanyAssistant.Web.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CompanyAssistant.Web.Tests;

[CollectionDefinition("PostgresDatabase", DisableParallelization = true)]
public sealed class PostgresDatabaseCollection : ICollectionFixture<PostgresDatabaseFixture>;

// Connects the real Npgsql stores to a disposable PostgreSQL/pgvector database.
// These tests are required in CI and fail (rather than skip) when the connection
// string is missing, so a missing configuration can never look like a pass.
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    public const string ConnectionStringEnvironmentVariable = "TEST_DATABASE_CONNECTION_STRING";

    public string ConnectionString { get; private set; } = string.Empty;

    internal DatabaseSettings DatabaseSettings { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Database integration tests require '{ConnectionStringEnvironmentVariable}' to point at a disposable " +
                "PostgreSQL/pgvector database. These tests are mandatory in CI and must not be skipped.");

        await using (var dataSource = NpgsqlDataSource.Create(ConnectionString))
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:MigrationConnectionString"] = ConnectionString
            })
            .Build();

        var result = await MigrationRunner.RunAsync(configuration, NullLogger.Instance);
        if (result != 0)
            throw new InvalidOperationException("Failed to apply database migrations for the integration tests.");

        DatabaseSettings = new DatabaseSettings(
            ConnectionString,
            ConnectionString,
            ConnectionString,
            DatabaseSettings.DefaultRuntimeRole,
            DatabaseSettings.DefaultIngestionRole);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task ResetAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "TRUNCATE TABLE documents, document_versions, document_chunks RESTART IDENTITY CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> CountVersionsAsync(string documentId)
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM document_versions WHERE document_id = $1", connection);
        command.Parameters.AddWithValue(documentId);
        return (int)(long)(await command.ExecuteScalarAsync())!;
    }

    internal async Task<StoredDocumentSnapshot?> GetStoredDocumentAsync(string documentId)
    {
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT content, effective_date, deleted_at FROM documents WHERE document_id = $1", connection);
        command.Parameters.AddWithValue(documentId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new StoredDocumentSnapshot(
            reader.GetString(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    internal sealed record StoredDocumentSnapshot(
        string Content,
        DateOnly EffectiveDate,
        DateTimeOffset? DeletedAt);

    internal NpgsqlRetrievalStore CreateRetrievalStore() => new(DatabaseSettings);

    internal NpgsqlIngestionStore CreateIngestionStore() => new(DatabaseSettings);
}
