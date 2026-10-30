namespace CompanyAssistant.Web.Knowledge;

internal sealed class RetrievalService(IRetrievalStore store)
{
    public Task<IReadOnlyList<RetrievedChunk>> GetAuthorizedChunksAsync(
        IReadOnlyCollection<long> chunkIds, RetrievalCaller caller, CancellationToken ct) =>
        store.GetAuthorizedChunksAsync(chunkIds, AccessPolicyMapping.AllowedFor(caller.Permissions), ct);

    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        float[] queryEmbedding,
        RetrievalCaller caller,
        int limit,
        CancellationToken ct) =>
        store.SearchAsync(queryEmbedding, AccessPolicyMapping.AllowedFor(caller.Permissions), limit, ct);

    public Task<SourceDocument?> GetSourceDocumentAsync(
        string documentId,
        RetrievalCaller caller,
        CancellationToken ct) =>
        store.GetSourceDocumentAsync(documentId, AccessPolicyMapping.AllowedFor(caller.Permissions), ct);
}
