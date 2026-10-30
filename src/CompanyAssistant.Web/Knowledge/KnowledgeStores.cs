namespace CompanyAssistant.Web.Knowledge;

internal interface IIngestionStore
{
    Task<StoredDocument?> GetDocumentAsync(string documentId, CancellationToken ct);

    Task UpsertAsync(IngestedDocument document, CancellationToken ct);

    Task<int> SoftDeleteMissingAsync(IReadOnlySet<string> seenSourcePaths, CancellationToken ct);
}

internal interface IRetrievalStore
{
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        float[] queryEmbedding,
        IReadOnlyCollection<string> allowedPolicies,
        int limit,
        CancellationToken ct);

    Task<SourceDocument?> GetSourceDocumentAsync(
        string documentId,
        IReadOnlyCollection<string> allowedPolicies,
        CancellationToken ct);
}
