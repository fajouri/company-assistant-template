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

        // Parse the entire input set before writing anything so duplicate document ids
        // can be rejected before any upsert, soft-delete or embedding call.
        var parsedFiles = new List<(MarkdownFile File, ParsedDocument Parsed)>();
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

            try
            {
                var bytes = File.ReadAllBytes(file.FullPath);
                parsedFiles.Add((file, MarkdownParser.Parse(bytes, options.MaxChunkChars)));
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }

        ThrowOnDuplicateDocumentIds(parsedFiles);

        var indexed = 0;
        var unchanged = 0;

        foreach (var (file, parsed) in parsedFiles)
        {
            ct.ThrowIfCancellationRequested();

            var existing = await store.GetDocumentAsync(parsed.Metadata.DocumentId, ct);
            if (IsUnchanged(existing, parsed, file.RelativePath))
            {
                unchanged++;
                continue;
            }

            // A metadata-only change (approval state, access policy, version, path) leaves
            // the body and its embeddings valid, so it can be applied without the embeddings
            // provider. This is what makes revocations and access-policy restrictions stick
            // even when embeddings are unavailable.
            if (existing is not null && existing.Content == parsed.Content)
            {
                await store.UpdateMetadataAsync(
                    new IngestedDocument(parsed.Metadata, file.RelativePath, parsed.Content, parsed.ContentSha256, []),
                    ct);
                indexed++;
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

    private static void ThrowOnDuplicateDocumentIds(IReadOnlyList<(MarkdownFile File, ParsedDocument Parsed)> parsedFiles)
    {
        var pathsById = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (file, parsed) in parsedFiles)
        {
            if (!pathsById.TryGetValue(parsed.Metadata.DocumentId, out var paths))
            {
                paths = [];
                pathsById[parsed.Metadata.DocumentId] = paths;
            }

            paths.Add(file.RelativePath);
        }

        var duplicates = pathsById
            .Where(pair => pair.Value.Count > 1)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"id '{pair.Key}' appears in {string.Join(", ", pair.Value.OrderBy(path => path, StringComparer.Ordinal).Select(path => $"'{path}'"))}")
            .ToList();

        if (duplicates.Count > 0)
            throw new InvalidOperationException($"Duplicate document ids detected: {string.Join("; ", duplicates)}.");
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
