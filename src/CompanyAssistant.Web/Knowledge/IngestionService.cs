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
        var pending = 0;
        var contentToIndex = new List<(MarkdownFile File, ParsedDocument Parsed)>();

        // Phase 1 — apply revocations, access restrictions, future-effective-date retirements and
        // metadata-only updates across the whole input set without depending on the embeddings
        // provider. Content that still requires embeddings is collected for phase 2, so a later
        // embedding failure cannot leave a revoked, restricted or future-dated previous version
        // queryable.
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

            // The content changed. If the incoming metadata removes access the previous version
            // granted (a revocation, a different access policy, or an effective date pushed into
            // the future), retire the previous version now so it cannot remain queryable under its
            // old approval state, access policy or effective date. The future-date check runs in
            // PostgreSQL against CURRENT_DATE so it matches the retrieval queries exactly.
            if (existing is not null && RetiresAccess(existing, parsed.Metadata))
            {
                await store.RetireAsync(existing.DocumentId, ct);
            }
            else if (existing is not null && existing.EffectiveDate != parsed.Metadata.EffectiveDate)
            {
                await store.RetireIfFutureEffectiveAsync(existing.DocumentId, parsed.Metadata.EffectiveDate, ct);
            }

            contentToIndex.Add((file, parsed));
        }

        // Phase 2 — index content that requires embeddings. Each document is attempted
        // independently so one embedding failure does not prevent the others.
        foreach (var (file, parsed) in contentToIndex)
        {
            ct.ThrowIfCancellationRequested();

            if (parsed.Chunks.Count > 0 && !embeddings.IsConfigured)
            {
                pending++;
                continue;
            }

            IReadOnlyList<IngestedChunk> chunks = [];
            if (parsed.Chunks.Count > 0)
            {
                IReadOnlyList<float[]> vectors;
                try
                {
                    vectors = await embeddings.EmbedAsync(
                        parsed.Chunks.Select(chunk => chunk.Content).ToArray(), ct);
                }
                catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException
                    || (ex is OperationCanceledException && !ct.IsCancellationRequested))
                {
                    pending++;
                    continue;
                }

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
        return new IngestionReport(indexed, unchanged, deleted, scan.Rejected, oversized, failed, pending);
    }

    private static bool RetiresAccess(StoredDocument existing, PolicyMetadata incoming) =>
        (existing.ApprovalState == ApprovalStates.Approved && incoming.ApprovalState != ApprovalStates.Approved)
        || existing.AccessPolicy != incoming.AccessPolicy;

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
