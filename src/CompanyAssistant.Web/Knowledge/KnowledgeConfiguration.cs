namespace CompanyAssistant.Web.Knowledge;

internal sealed record EmbeddingSettings(
    string Endpoint,
    string ApiKey,
    string Deployment,
    string ApiVersion,
    int Dimensions)
{
    public const int DefaultDimensions = 1536;
    private const string DefaultApiVersion = "2024-02-01";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Deployment);

    public static EmbeddingSettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Embeddings");
        var endpoint = (section["Endpoint"] ?? string.Empty).Trim();
        var apiKey = section["ApiKey"] ?? string.Empty;
        var deployment = (section["Deployment"] ?? string.Empty).Trim();
        var apiVersion = section["ApiVersion"];
        apiVersion = string.IsNullOrWhiteSpace(apiVersion) ? DefaultApiVersion : apiVersion;

        var dimensionsText = section["Dimensions"];
        var dimensions = DefaultDimensions;
        if (!string.IsNullOrWhiteSpace(dimensionsText) && !int.TryParse(dimensionsText, out dimensions))
            throw new InvalidOperationException("Embeddings:Dimensions must be an integer.");

        var any = !string.IsNullOrWhiteSpace(endpoint)
            || !string.IsNullOrWhiteSpace(apiKey)
            || !string.IsNullOrWhiteSpace(deployment);
        var all = !string.IsNullOrWhiteSpace(endpoint)
            && !string.IsNullOrWhiteSpace(apiKey)
            && !string.IsNullOrWhiteSpace(deployment);

        if (any && !all)
            throw new InvalidOperationException(
                "Embeddings configuration is incomplete: set Embeddings:Endpoint, Embeddings:ApiKey and Embeddings:Deployment together, or leave all three empty.");

        if (all)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || string.IsNullOrEmpty(uri.Host))
                throw new InvalidOperationException("Embeddings:Endpoint must be an absolute HTTP(S) URL.");

            if (dimensions != DefaultDimensions)
                throw new InvalidOperationException(
                    $"Embeddings:Dimensions must be {DefaultDimensions} with the current schema; add a versioned migration before changing the vector dimension.");
        }

        return new EmbeddingSettings(endpoint, apiKey, deployment, apiVersion, dimensions);
    }
}

internal sealed record IngestionOptions(string SourcePath, int MaxFileBytes, int MaxChunkChars)
{
    public const int DefaultMaxFileBytes = 1_048_576;
    public const int DefaultMaxChunkChars = 6_000;

    public static IngestionOptions Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Ingestion");
        var sourcePath = (section["SourcePath"] ?? string.Empty).Trim();
        var maxFileBytes = int.TryParse(section["MaxFileBytes"], out var fb) && fb > 0 ? fb : DefaultMaxFileBytes;
        var maxChunkChars = int.TryParse(section["MaxChunkChars"], out var cc) && cc > 0 ? cc : DefaultMaxChunkChars;
        return new IngestionOptions(sourcePath, maxFileBytes, maxChunkChars);
    }
}
