namespace CompanyAssistant.Web.Knowledge;

internal sealed record DatabaseSettings(
    string? RuntimeConnectionString,
    string? IngestionConnectionString,
    string? MigrationConnectionString,
    string RuntimeRole,
    string IngestionRole)
{
    public const string DefaultRuntimeRole = "assistant_runtime";
    public const string DefaultIngestionRole = "assistant_ingest";

    public bool IsRuntimeConfigured => !string.IsNullOrWhiteSpace(RuntimeConnectionString);
    public bool IsIngestionConfigured => !string.IsNullOrWhiteSpace(IngestionConnectionString);

    public static DatabaseSettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Database");
        var runtime = NullIfEmpty(section["RuntimeConnectionString"]);
        var ingestion = NullIfEmpty(section["IngestionConnectionString"]);
        var migration = NullIfEmpty(section["MigrationConnectionString"]);
        var runtimeRole = string.IsNullOrWhiteSpace(section["RuntimeRole"]) ? DefaultRuntimeRole : section["RuntimeRole"]!;
        var ingestionRole = string.IsNullOrWhiteSpace(section["IngestionRole"]) ? DefaultIngestionRole : section["IngestionRole"]!;

        ValidateRoleName(runtimeRole);
        ValidateRoleName(ingestionRole);

        return new DatabaseSettings(runtime, ingestion, migration, runtimeRole, ingestionRole);
    }

    public static string RequireRuntime(DatabaseSettings settings) =>
        settings.RuntimeConnectionString
        ?? throw new InvalidOperationException("Database:RuntimeConnectionString is required for retrieval.");

    public static string RequireIngestion(DatabaseSettings settings) =>
        settings.IngestionConnectionString
        ?? throw new InvalidOperationException("Database:IngestionConnectionString is required for ingestion.");

    public static string RequireMigration(DatabaseSettings settings) =>
        settings.MigrationConnectionString
        ?? settings.IngestionConnectionString
        ?? settings.RuntimeConnectionString
        ?? throw new InvalidOperationException("Database:MigrationConnectionString (or an ingestion/runtime connection) is required for migrations.");

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void ValidateRoleName(string role)
    {
        if (string.IsNullOrWhiteSpace(role)
            || role.Length == 0
            || char.IsDigit(role[0])
            || !role.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
            throw new InvalidOperationException($"Invalid database role name '{role}'. Use letters, digits and underscores, starting with a letter.");
    }
}
