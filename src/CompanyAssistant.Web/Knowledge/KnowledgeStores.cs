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

    // Retires (soft-deletes) a document so it stops being retrievable, without touching its
    // chunks or version history. Used when a content change coincides with a revocation or
    // access restriction and the new content cannot yet be embedded; a later successful
    // ingestion re-publishes the document.
    Task RetireAsync(string documentId, CancellationToken ct);

    // Retires the currently effective previous version when the incoming effective date is in
    // the future relative to PostgreSQL's CURRENT_DATE (the same reference the retrieval queries
    // use). Used when a content change pushes the effective date forward and the new content
    // cannot yet be embedded, so the stale previous version cannot stay queryable.
    Task RetireIfFutureEffectiveAsync(string documentId, DateOnly incomingEffectiveDate, CancellationToken ct);

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
