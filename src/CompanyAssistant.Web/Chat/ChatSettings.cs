namespace CompanyAssistant.Web.Chat;

internal sealed record ChatSettings(
    string Endpoint,
    string ApiKey,
    string Deployment,
    string ApiVersion,
    int MaxContextChunks,
    int MaxContextChars,
    int MaxHistoryTurns,
    int MaxQueryLength,
    int MaxOutputTokens,
    int TimeoutSeconds,
    int RequestsPerMinute)
{
    // Cosine distance from pgvector (<=>); deployment-specific calibration is required.
    public double MaxEvidenceDistance { get; init; } = 0.35;
    public const int DefaultMaxContextChunks = 4;
    public const int DefaultMaxContextChars = 8_000;
    public const int DefaultMaxHistoryTurns = 4;
    public const int DefaultMaxQueryLength = 2_000;
    public const int DefaultMaxOutputTokens = 512;
    public const int DefaultTimeoutSeconds = 30;
    public const int DefaultRequestsPerMinute = 10;
    private const string DefaultApiVersion = "2024-02-01";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Deployment);

    public static ChatSettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Chat");
        var endpoint = (section["Endpoint"] ?? string.Empty).Trim();
        var apiKey = section["ApiKey"] ?? string.Empty;
        var deployment = (section["Deployment"] ?? string.Empty).Trim();
        var apiVersion = string.IsNullOrWhiteSpace(section["ApiVersion"]) ? DefaultApiVersion : section["ApiVersion"]!;

        var any = !string.IsNullOrWhiteSpace(endpoint)
            || !string.IsNullOrWhiteSpace(apiKey)
            || !string.IsNullOrWhiteSpace(deployment);
        var all = !string.IsNullOrWhiteSpace(endpoint)
            && !string.IsNullOrWhiteSpace(apiKey)
            && !string.IsNullOrWhiteSpace(deployment);

        if (any && !all)
            throw new InvalidOperationException(
                "Chat configuration is incomplete: set Chat:Endpoint, Chat:ApiKey and Chat:Deployment together, or leave all three empty.");

        if (all
            && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || string.IsNullOrEmpty(uri.Host)))
            throw new InvalidOperationException("Chat:Endpoint must be an absolute HTTP(S) URL.");

        return new ChatSettings(
            endpoint,
            apiKey,
            deployment,
            apiVersion,
            ReadBoundedInt(section, "MaxContextChunks", DefaultMaxContextChunks, 1, 20),
            ReadBoundedInt(section, "MaxContextChars", DefaultMaxContextChars, 1_000, 100_000),
            ReadBoundedInt(section, "MaxHistoryTurns", DefaultMaxHistoryTurns, 0, 50),
            ReadBoundedInt(section, "MaxQueryLength", DefaultMaxQueryLength, 1, 100_000),
            ReadBoundedInt(section, "MaxOutputTokens", DefaultMaxOutputTokens, 16, 16_384),
            ReadBoundedInt(section, "TimeoutSeconds", DefaultTimeoutSeconds, 5, 600),
            ReadBoundedInt(section, "RequestsPerMinute", DefaultRequestsPerMinute, 1, 10_000))
        {
            MaxEvidenceDistance = ReadDistance(section)
        };
    }

    private static double ReadDistance(IConfigurationSection section)
    {
        var value = section["MaxEvidenceDistance"];
        if (string.IsNullOrWhiteSpace(value)) return 0.35;
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var distance)
            || !double.IsFinite(distance) || distance < 0 || distance > 2)
            throw new InvalidOperationException("Chat:MaxEvidenceDistance must be between 0 and 2.");
        return distance;
    }

    private static int ReadBoundedInt(IConfigurationSection section, string key, int fallback, int min, int max)
    {
        var text = section[key];
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        if (!int.TryParse(text, out var value))
            throw new InvalidOperationException($"Chat:{key} must be an integer.");
        if (value < min || value > max)
            throw new InvalidOperationException($"Chat:{key} must be between {min} and {max}.");
        return value;
    }
}
