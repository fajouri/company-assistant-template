using CompanyAssistant.Web.Knowledge;
using Npgsql;

namespace CompanyAssistant.Web.Tests;

[Trait("Category", "Database")]
[Collection("PostgresDatabase")]
public sealed class NpgsqlRetrievalStoreIntegrationTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task SearchAsync_returns_approved_chunks_for_allowed_policies_without_exception()
    {
        await fixture.ResetAsync();
        await SeedAsync("it-employee", "# Leave\n\nEmployee body.\n", AccessPolicies.Employee);
        await SeedAsync("it-hr", "# Contacts\n\nHR body.\n", AccessPolicies.Hr);

        await using var store = fixture.CreateRetrievalStore();

        var employeeResults = await store.SearchAsync(
            FakeEmbeddingClient.VectorFor("# Leave\n\nEmployee body."),
            [AccessPolicies.Employee],
            10,
            CancellationToken.None);
        var hrResults = await store.SearchAsync(
            FakeEmbeddingClient.VectorFor("# Contacts\n\nHR body."),
            [AccessPolicies.Hr],
            10,
            CancellationToken.None);

        Assert.Contains(employeeResults, chunk => chunk.DocumentId == "it-employee");
        Assert.DoesNotContain(employeeResults, chunk => chunk.DocumentId == "it-hr");
        Assert.Contains(hrResults, chunk => chunk.DocumentId == "it-hr");
        Assert.DoesNotContain(hrResults, chunk => chunk.DocumentId == "it-employee");
    }

    [Fact]
    public async Task SearchAsync_returns_empty_when_no_document_matches_policy_without_exception()
    {
        await fixture.ResetAsync();
        await SeedAsync("it-hr", "# Contacts\n\nHR body.\n", AccessPolicies.Hr);

        await using var store = fixture.CreateRetrievalStore();

        var results = await store.SearchAsync(
            FakeEmbeddingClient.VectorFor("# Contacts\n\nHR body."),
            [AccessPolicies.Employee],
            10,
            CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_hides_revoked_and_future_documents_without_exception()
    {
        await fixture.ResetAsync();
        await SeedAsync("it-approved", "# A\n\nApproved body.\n", AccessPolicies.Employee);
        await SeedAsync("it-revoked", "# R\n\nRevoked body.\n", AccessPolicies.Employee, ApprovalStates.Revoked);
        await SeedAsync("it-future", "# F\n\nFuture body.\n", AccessPolicies.Employee,
            ApprovalStates.Approved, new DateOnly(2999, 1, 1));

        await using var store = fixture.CreateRetrievalStore();

        var results = await store.SearchAsync(
            FakeEmbeddingClient.VectorFor("# A\n\nApproved body."),
            [AccessPolicies.Employee],
            10,
            CancellationToken.None);

        Assert.Contains(results, chunk => chunk.DocumentId == "it-approved");
        Assert.DoesNotContain(results, chunk => chunk.DocumentId == "it-revoked");
        Assert.DoesNotContain(results, chunk => chunk.DocumentId == "it-future");
    }

    [Fact]
    public async Task GetSourceDocumentAsync_returns_document_when_allowed_without_exception()
    {
        await fixture.ResetAsync();
        await SeedAsync("it-employee", "# Leave\n\nEmployee body.\n", AccessPolicies.Employee);

        await using var store = fixture.CreateRetrievalStore();

        var document = await store.GetSourceDocumentAsync(
            "it-employee", [AccessPolicies.Employee], CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal("it-employee", document.DocumentId);
        Assert.Equal("# Leave\n\nEmployee body.\n", document.Content);
    }

    [Fact]
    public async Task GetSourceDocumentAsync_returns_null_when_not_found_without_exception()
    {
        await fixture.ResetAsync();
        await SeedAsync("it-employee", "# Leave\n\nEmployee body.\n", AccessPolicies.Employee);

        await using var store = fixture.CreateRetrievalStore();

        var document = await store.GetSourceDocumentAsync(
            "it-missing", [AccessPolicies.Employee], CancellationToken.None);

        Assert.Null(document);
    }

    [Fact]
    public async Task GetSourceDocumentAsync_returns_null_when_denied_by_policy_without_exception()
    {
        await fixture.ResetAsync();
        await SeedAsync("it-hr", "# Contacts\n\nHR body.\n", AccessPolicies.Hr);

        await using var store = fixture.CreateRetrievalStore();

        var document = await store.GetSourceDocumentAsync(
            "it-hr", [AccessPolicies.Employee], CancellationToken.None);

        Assert.Null(document);
    }

    [Fact]
    public async Task GetSourceDocumentAsync_returns_null_for_revoked_or_future_documents_without_exception()
    {
        await fixture.ResetAsync();
        await SeedAsync("it-revoked", "# R\n\nRevoked body.\n", AccessPolicies.Employee, ApprovalStates.Revoked);
        await SeedAsync("it-future", "# F\n\nFuture body.\n", AccessPolicies.Employee,
            ApprovalStates.Approved, new DateOnly(2999, 1, 1));

        await using var store = fixture.CreateRetrievalStore();

        Assert.Null(await store.GetSourceDocumentAsync("it-revoked", [AccessPolicies.Employee], CancellationToken.None));
        Assert.Null(await store.GetSourceDocumentAsync("it-future", [AccessPolicies.Employee], CancellationToken.None));
    }

    [Fact]
    public async Task Authorized_chunk_recheck_uses_current_document_state()
    {
        await fixture.ResetAsync();
        await SeedAsync("it-current", "Current body", AccessPolicies.Employee);
        await using var store = fixture.CreateRetrievalStore();
        var initial = await store.SearchAsync(FakeEmbeddingClient.VectorFor("Current body"),
            [AccessPolicies.Employee], 1, CancellationToken.None);
        var id = Assert.Single(initial).ChunkId;
        Assert.Single(await store.GetAuthorizedChunksAsync([id], [AccessPolicies.Employee], CancellationToken.None));

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "UPDATE documents SET access_policy = 'hr' WHERE document_id = 'it-current'", connection);
            await command.ExecuteNonQueryAsync();
        }
        Assert.Empty(await store.GetAuthorizedChunksAsync([id], [AccessPolicies.Employee], CancellationToken.None));
        Assert.Single(await store.GetAuthorizedChunksAsync([id], [AccessPolicies.Hr], CancellationToken.None));
    }

    [Fact]
    public async Task Cosine_distance_distinguishes_relevant_and_irrelevant_authorized_chunks()
    {
        await fixture.ResetAsync();
        var first = new float[1536];
        first[0] = 1;
        var other = new float[1536];
        other[1] = 1;
        await SeedAsync("it-relevant", "Relevant body", AccessPolicies.Employee, embedding: first);
        await SeedAsync("it-irrelevant", "Unrelated body", AccessPolicies.Employee, embedding: other);
        await using var store = fixture.CreateRetrievalStore();
        var results = await store.SearchAsync(first, [AccessPolicies.Employee], 2, CancellationToken.None);
        Assert.Equal("it-relevant", results[0].DocumentId);
        Assert.True(results[0].Distance <= 0.35);
        Assert.True(results[1].Distance > 0.35);
    }

    private async Task SeedAsync(
        string documentId,
        string content,
        string accessPolicy,
        string approvalState = ApprovalStates.Approved,
        DateOnly? effectiveDate = null,
        float[]? embedding = null)
    {
        var metadata = new PolicyMetadata(
            documentId,
            $"{documentId} policy",
            "Owner",
            "Approver",
            "1.0",
            effectiveDate ?? new DateOnly(2025, 1, 1),
            new DateOnly(2026, 6, 30),
            approvalState,
            accessPolicy);

        embedding ??= FakeEmbeddingClient.VectorFor(content);
        var chunks = new List<IngestedChunk>
        {
            new(null, content, Hashing.Sha256Hex(content), embedding)
        };

        var document = new IngestedDocument(
            metadata,
            $"{documentId}.md",
            content,
            Hashing.Sha256Hex(content),
            chunks);

        await using var store = fixture.CreateIngestionStore();
        await store.UpsertAsync(document, CancellationToken.None);
    }
}
