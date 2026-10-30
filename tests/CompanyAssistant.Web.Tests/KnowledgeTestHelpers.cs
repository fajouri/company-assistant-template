using CompanyAssistant.Web.Knowledge;

namespace CompanyAssistant.Web.Tests;

internal sealed class FakeEmbeddingClient(int dimensions = 1536) : IEmbeddingClient
{
    public bool IsConfigured => true;

    public HashSet<string> FailOnTexts { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Any(text => FailOnTexts.Any(marker => text.Contains(marker, StringComparison.Ordinal))))
            throw new InvalidOperationException("Forced embedding failure.");

        return Task.FromResult<IReadOnlyList<float[]>>(
            texts.Select(text => VectorFor(text, dimensions)).ToList());
    }

    private static float[] VectorFor(string text, int dimensionCount)
    {
        var vector = new float[dimensionCount];
        var hash = StableHash(text);
        for (var i = 0; i < dimensionCount; i++)
            vector[i] = ((hash * 31 + i) % 1000) / 1000f;
        return vector;
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in text)
                hash = hash * 31 + character;
            return hash;
        }
    }
}

internal sealed class FakeIngestionStore : IIngestionStore
{
    public Dictionary<string, StoredDocument> Documents { get; } = new(StringComparer.Ordinal);
    public List<IngestedDocument> Upserts { get; } = new();
    public List<IReadOnlySet<string>> SoftDeleteCalls { get; } = new();
    public Action<IngestedDocument>? OnUpsert { get; set; }

    public Task<StoredDocument?> GetDocumentAsync(string documentId, CancellationToken ct) =>
        Task.FromResult(Documents.TryGetValue(documentId, out var document) ? document : null);

    public Task UpsertAsync(IngestedDocument document, CancellationToken ct)
    {
        OnUpsert?.Invoke(document);
        Upserts.Add(document);
        Documents[document.Metadata.DocumentId] = ToStored(document, deletedAt: null);
        return Task.CompletedTask;
    }

    public Task<int> SoftDeleteMissingAsync(IReadOnlySet<string> seenSourcePaths, CancellationToken ct)
    {
        SoftDeleteCalls.Add(seenSourcePaths);
        var count = 0;
        foreach (var key in Documents.Keys.ToList())
        {
            var document = Documents[key];
            if (document.DeletedAt is null && !seenSourcePaths.Contains(document.SourcePath))
            {
                Documents[key] = document with { DeletedAt = DateTimeOffset.UtcNow };
                count++;
            }
        }

        return Task.FromResult(count);
    }

    public static StoredDocument ToStored(IngestedDocument document, DateTimeOffset? deletedAt) => new(
        document.Metadata.DocumentId,
        document.SourcePath,
        document.ContentSha256,
        document.Content,
        document.Metadata.Title,
        document.Metadata.Owner,
        document.Metadata.Approver,
        document.Metadata.Version,
        document.Metadata.EffectiveDate,
        document.Metadata.ReviewDate,
        document.Metadata.ApprovalState,
        document.Metadata.AccessPolicy,
        deletedAt);
}

internal sealed class FakeRetrievalStore : IRetrievalStore
{
    public List<(RetrievedChunk Chunk, string Policy)> Chunks { get; } = new();
    public List<(SourceDocument Document, string Policy)> Sources { get; } = new();
    public List<IReadOnlyCollection<string>> SearchPolicyCalls { get; } = new();
    public List<IReadOnlyCollection<string>> SourcePolicyCalls { get; } = new();

    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        float[] queryEmbedding,
        IReadOnlyCollection<string> allowedPolicies,
        int limit,
        CancellationToken ct)
    {
        SearchPolicyCalls.Add(allowedPolicies);
        var result = Chunks
            .Where(entry => allowedPolicies.Contains(entry.Policy))
            .Select(entry => entry.Chunk)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<RetrievedChunk>>(result);
    }

    public Task<SourceDocument?> GetSourceDocumentAsync(
        string documentId,
        IReadOnlyCollection<string> allowedPolicies,
        CancellationToken ct)
    {
        SourcePolicyCalls.Add(allowedPolicies);
        var match = Sources.FirstOrDefault(entry =>
            entry.Document.DocumentId == documentId && allowedPolicies.Contains(entry.Policy));
        return Task.FromResult<SourceDocument?>(match.Document);
    }

    public static RetrievedChunk ChunkFor(string documentId, string title, string content, string policy) =>
        new(1, documentId, title, "1.0", 0, null, content, 0.1);
}

internal static class MarkdownFixtures
{
    public static string Policy(
        string id,
        string approvalState = "approved",
        string accessPolicy = "employee",
        string effectiveDate = "2025-01-01",
        string? body = null) =>
        $"""
        ---
        id: {id}
        title: {id} policy
        owner: Fictional Owner
        approver: Fictional Approver
        version: "1.0"
        effective_date: {effectiveDate}
        review_date: 2026-06-30
        approval_state: {approvalState}
        access_policy: {accessPolicy}
        ---

        {body ?? "# Overview\n\nFictional policy body.\n"}
        """;
}
