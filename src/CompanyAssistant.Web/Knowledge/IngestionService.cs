namespace CompanyAssistant.Web.Knowledge;

internal sealed class IngestionService(
    IIngestionStore store,
    IEmbeddingClient embeddings,
    IngestionOptions options)
{
    public async Task<IngestionReport> RunAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.SourcePath))
            throw new InvalidOperationException(
                "Ingestion:SourcePath is required. Configure the read-only Markdown source folder.");

        var scan = new MarkdownSourceScanner().Scan(options.SourcePath);
        var seenPaths = scan.Files.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);

        var indexed = 0;
        var unchanged = 0;
        var oversized = 0;
        var failed = 0;

        foreach (var file in scan.Files)
        {
            ct.ThrowIfCancellationRequested();

            var info = new FileInfo(file.FullPath);
            if (info.Length > options.MaxFileBytes)
            {
                oversized++;
                continue;
            }

            ParsedDocument parsed;
            try
            {
                var bytes = File.ReadAllBytes(file.FullPath);
                parsed = MarkdownParser.Parse(bytes, options.MaxChunkChars);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                failed++;
                continue;
            }

            var existing = await store.GetDocumentAsync(parsed.Metadata.DocumentId, ct);
            if (IsUnchanged(existing, parsed, file.RelativePath))
            {
                unchanged++;
                continue;
            }

            IReadOnlyList<IngestedChunk> chunks = [];
            if (parsed.Chunks.Count > 0)
            {
                if (!embeddings.IsConfigured)
                    throw new InvalidOperationException("Embeddings are not configured; cannot index Markdown content.");

                var vectors = await embeddings.EmbedAsync(
                    parsed.Chunks.Select(chunk => chunk.Content).ToArray(), ct);

                chunks = parsed.Chunks.Zip(vectors, (chunk, vector) =>
                    new IngestedChunk(chunk.SectionHeading, chunk.Content, Hashing.Sha256Hex(chunk.Content), vector))
                    .ToArray();
            }

            await store.UpsertAsync(
                new IngestedDocument(parsed.Metadata, file.RelativePath, parsed.Content, parsed.ContentSha256, chunks),
                ct);

            indexed++;
        }

        var deleted = await store.SoftDeleteMissingAsync(seenPaths, ct);
        return new IngestionReport(indexed, unchanged, deleted, scan.Rejected, oversized, failed);
    }

    private static bool IsUnchanged(StoredDocument? existing, ParsedDocument incoming, string sourcePath) =>
        existing is not null
        && existing.DeletedAt is null
        && existing.SourcePath == sourcePath
        && existing.ContentSha256 == incoming.ContentSha256
        && existing.Title == incoming.Metadata.Title
        && existing.Owner == incoming.Metadata.Owner
        && existing.Approver == incoming.Metadata.Approver
        && existing.Version == incoming.Metadata.Version
        && existing.EffectiveDate == incoming.Metadata.EffectiveDate
        && existing.ReviewDate == incoming.Metadata.ReviewDate
        && existing.ApprovalState == incoming.Metadata.ApprovalState
        && existing.AccessPolicy == incoming.Metadata.AccessPolicy;
}
