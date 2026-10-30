using CompanyAssistant.Web.Knowledge;

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

    private async Task SeedAsync(
        string documentId,
        string content,
        string accessPolicy,
        string approvalState = ApprovalStates.Approved,
        DateOnly? effectiveDate = null)
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

        var embedding = FakeEmbeddingClient.VectorFor(content);
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
