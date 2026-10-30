using System.Reflection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;

namespace CompanyAssistant.Web.Knowledge;

internal static class MigrationRunner
{
    private const string ResourcePrefix = "Knowledge.Migrations.";

    public static async Task<int> RunAsync(IConfiguration configuration, ILogger logger)
    {
        var settings = DatabaseSettings.Load(configuration);
        var connectionString = DatabaseSettings.RequireMigration(settings);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        await EnsureVectorExtensionAsync(connection, logger);
        await EnsureMigrationsTableAsync(connection);

        var migrations = ReadEmbeddedMigrations();
        foreach (var migration in migrations)
        {
            if (await IsAppliedAsync(connection, migration.Name))
                continue;
            logger.LogInformation("Applying migration {Migration}.", migration.Name);
            GuardNonDestructive(migration.Sql);
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(migration.Sql, connection, transaction))
                await command.ExecuteNonQueryAsync();
            await using (var insert = new NpgsqlCommand(
                "INSERT INTO __schema_migrations (version, checksum) VALUES ($1, $2)", connection, transaction))
            {
                insert.Parameters.AddWithValue(migration.Name);
                insert.Parameters.AddWithValue(Hashing.Sha256Hex(migration.Sql));
                await insert.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }

        await SelfCheckVectorMappingAsync(connectionString);
        logger.LogInformation("Migrations up to date ({Count} script(s)).", migrations.Count);
        return 0;
    }

    private static async Task EnsureVectorExtensionAsync(NpgsqlConnection connection, ILogger logger)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM pg_extension WHERE extname = 'vector'", connection);
        if (await command.ExecuteScalarAsync() is null)
        {
            logger.LogError("The pgvector 'vector' extension is missing; run deploy/init.sql first.");
            throw new InvalidOperationException("The pgvector 'vector' extension is required.");
        }
    }

    private static async Task EnsureMigrationsTableAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "CREATE TABLE IF NOT EXISTS __schema_migrations (" +
            "version text PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static IReadOnlyList<(string Name, string Sql)> ReadEmbeddedMigrations()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (Name: name[ResourcePrefix.Length..], Sql: ReadResource(assembly, name)))
            .ToList();
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded migration resource '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<bool> IsAppliedAsync(NpgsqlConnection connection, string name)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM __schema_migrations WHERE version = $1", connection);
        command.Parameters.AddWithValue(name);
        return await command.ExecuteScalarAsync() is not null;
    }

    internal static void GuardNonDestructive(string sql)
    {
        foreach (var statement in sql.Split(';'))
        {
            var upper = statement.ToUpperInvariant();
            if (ContainsWord(upper, "DROP")
                || ContainsWord(upper, "TRUNCATE")
                || upper.Contains("DELETE FROM", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Destructive statement (DROP/TRUNCATE/DELETE FROM) not allowed in migrations: '{statement.Trim()}'.");
        }
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.Ordinal);
        while (index >= 0)
        {
            var after = index + word.Length;
            var afterBoundary = after >= text.Length || !(char.IsLetterOrDigit(text[after]) || text[after] == '_');
            var beforeBoundary = index == 0 || !(char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_');
            if (beforeBoundary && afterBoundary)
                return true;
            index = text.IndexOf(word, index + 1, StringComparison.Ordinal);
        }
        return false;
    }

    private static async Task SelfCheckVectorMappingAsync(string connectionString)
    {
        await using var dataSource = NpgsqlDataSources.CreateVectorDataSource(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT $1 <=> $2", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = new Vector(new float[] { 1f, 0f, 0f }) });
        command.Parameters.Add(new NpgsqlParameter { Value = new Vector(new float[] { 1f, 0f, 0f }) });
        var distance = (double)(await command.ExecuteScalarAsync())!;
        if (Math.Abs(distance) > 0.0001)
            throw new InvalidOperationException($"pgvector self-check failed (cosine distance {distance}).");
    }
}
