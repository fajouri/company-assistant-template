namespace CompanyAssistant.Web.Knowledge;

internal interface IIngestionStore
{
    Task<StoredDocument?> GetDocumentAsync(string documentId, CancellationToken ct);

    Task UpsertAsync(IngestedDocument document, CancellationToken ct);

    // Applies metadata-only changes (approval state, access policy, version, path, ...)
    // without touching chunks or embeddings. Used for revocations and access-policy
    // restrictions where the body has not changed, so the document stays consistent
    // even when the embeddings provider is unavailable.
    Task UpdateMetadataAsync(IngestedDocument document, CancellationToken ct);

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
