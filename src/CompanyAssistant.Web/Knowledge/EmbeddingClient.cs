using System.Net.Http.Json;

namespace CompanyAssistant.Web.Knowledge;

internal interface IEmbeddingClient
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

// HTTP client for a configurable Foundry/Azure-OpenAI-compatible embeddings deployment.
// It fails closed: when Embeddings configuration is absent it throws rather than
// substituting a mock. Tests inject their own deterministic IEmbeddingClient.
internal sealed class FoundryEmbeddingClient(EmbeddingSettings settings, HttpClient http) : IEmbeddingClient
{
    public bool IsConfigured => settings.IsConfigured;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (!settings.IsConfigured)
            throw new InvalidOperationException(
                "Embeddings are not configured. Set Embeddings:Endpoint, Embeddings:ApiKey and Embeddings:Deployment before embedding content.");

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl());
        request.Headers.TryAddWithoutValidation("api-key", settings.ApiKey);
        request.Content = JsonContent.Create(new { input = texts });

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Embedding request failed with status {(int)response.StatusCode}: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct)
            ?? throw new InvalidOperationException("Embedding response was empty.");

        if (payload.Data is null || payload.Data.Count != texts.Count)
            throw new InvalidOperationException(
                $"Embedding response returned {payload.Data?.Count ?? 0} vectors for {texts.Count} inputs.");

        var result = new List<float[]>(payload.Data.Count);
        foreach (var item in payload.Data.OrderBy(item => item.Index))
        {
            if (item.Embedding is null || item.Embedding.Length != settings.Dimensions)
                throw new InvalidOperationException(
                    $"Embedding dimension {item.Embedding?.Length ?? 0} does not match the configured {settings.Dimensions}.");
            result.Add(item.Embedding);
        }

        return result;
    }

    private string BuildUrl()
    {
        var endpoint = settings.Endpoint.TrimEnd('/');
        return $"{endpoint}/openai/deployments/{Uri.EscapeDataString(settings.Deployment)}/embeddings?api-version={Uri.EscapeDataString(settings.ApiVersion)}";
    }

    private sealed class EmbeddingResponse
    {
        public List<EmbeddingItem>? Data { get; set; }
    }

    private sealed class EmbeddingItem
    {
        public int Index { get; set; }
        public float[]? Embedding { get; set; }
    }
}
