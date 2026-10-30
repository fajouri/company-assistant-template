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

    // Retires the previous version when a content change delays the effective date into the
    // future (incoming date later than the stored date and still in the future relative to
    // PostgreSQL's CURRENT_DATE, the same reference the retrieval queries use). This covers both
    // a currently effective version pushed into the future and a future version pushed further
    // into the future, whose earlier date would otherwise become visible before the new date.
    Task RetireIfFutureEffectiveAsync(string documentId, DateOnly incomingEffectiveDate, CancellationToken ct);

    Task<int> SoftDeleteMissingAsync(IReadOnlySet<string> seenSourcePaths, CancellationToken ct);
}

internal interface IRetrievalStore
{
    Task<IReadOnlyList<RetrievedChunk>> GetAuthorizedChunksAsync(
        IReadOnlyCollection<long> chunkIds,
        IReadOnlyCollection<string> allowedPolicies,
        CancellationToken ct);

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
