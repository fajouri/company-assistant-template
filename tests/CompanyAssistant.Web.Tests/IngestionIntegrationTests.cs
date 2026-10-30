using CompanyAssistant.Web.Knowledge;

namespace CompanyAssistant.Web.Tests;

[Trait("Category", "Database")]
[Collection("PostgresDatabase")]
public sealed class IngestionIntegrationTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Revocation_removes_document_from_search_when_embeddings_fail()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("it-revoke", body: "# Revoke\n\nRevoke body.\n"));

        var embeddings = new FakeEmbeddingClient();
        await using var store = fixture.CreateIngestionStore();
        var service = new IngestionService(store, embeddings, new IngestionOptions(temp.Path, 1_048_576, 6000));
        await service.RunAsync(CancellationToken.None);

        await using var retrieval = fixture.CreateRetrievalStore();
        Assert.True(await IsSearchableAsync(retrieval, "it-revoke", "# Revoke\n\nRevoke body.", [AccessPolicies.Employee]));

        File.WriteAllText(path, MarkdownFixtures.Policy("it-revoke", approvalState: "revoked", body: "# Revoke\n\nRevoke body.\n"));
        embeddings.FailOnTexts.Add("Revoke");
        await service.RunAsync(CancellationToken.None);

        Assert.False(await IsSearchableAsync(retrieval, "it-revoke", "# Revoke\n\nRevoke body.", [AccessPolicies.Employee]));
        Assert.Null(await retrieval.GetSourceDocumentAsync("it-revoke", [AccessPolicies.Employee], CancellationToken.None));
    }

    [Fact]
    public async Task Access_restriction_removes_employee_access_when_embeddings_fail()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("it-restrict", body: "# Restrict\n\nRestrict body.\n"));

        var embeddings = new FakeEmbeddingClient();
        await using var store = fixture.CreateIngestionStore();
        var service = new IngestionService(store, embeddings, new IngestionOptions(temp.Path, 1_048_576, 6000));
        await service.RunAsync(CancellationToken.None);

        await using var retrieval = fixture.CreateRetrievalStore();
        Assert.True(await IsSearchableAsync(retrieval, "it-restrict", "# Restrict\n\nRestrict body.", [AccessPolicies.Employee]));
        Assert.False(await IsSearchableAsync(retrieval, "it-restrict", "# Restrict\n\nRestrict body.", [AccessPolicies.Hr]));

        File.WriteAllText(path, MarkdownFixtures.Policy("it-restrict", accessPolicy: "hr", body: "# Restrict\n\nRestrict body.\n"));
        embeddings.FailOnTexts.Add("Restrict");
        await service.RunAsync(CancellationToken.None);

        Assert.False(await IsSearchableAsync(retrieval, "it-restrict", "# Restrict\n\nRestrict body.", [AccessPolicies.Employee]));
        Assert.True(await IsSearchableAsync(retrieval, "it-restrict", "# Restrict\n\nRestrict body.", [AccessPolicies.Hr]));
    }

    [Fact]
    public async Task Content_change_does_not_publish_partial_version_when_embeddings_fail()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("it-content", body: "# One\n\nOriginal body.\n"));

        var embeddings = new FakeEmbeddingClient();
        await using var store = fixture.CreateIngestionStore();
        var service = new IngestionService(store, embeddings, new IngestionOptions(temp.Path, 1_048_576, 6000));
        await service.RunAsync(CancellationToken.None);
        var versionsBefore = await fixture.CountVersionsAsync("it-content");

        await using var retrieval = fixture.CreateRetrievalStore();
        var source = await retrieval.GetSourceDocumentAsync("it-content", [AccessPolicies.Employee], CancellationToken.None);
        Assert.NotNull(source);
        Assert.Equal("# One\n\nOriginal body.", source.Content);

        File.WriteAllText(path, MarkdownFixtures.Policy("it-content", body: "# Two\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(CancellationToken.None));

        var after = await retrieval.GetSourceDocumentAsync("it-content", [AccessPolicies.Employee], CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal("# One\n\nOriginal body.", after.Content);
        Assert.Equal(versionsBefore, await fixture.CountVersionsAsync("it-content"));
    }

    [Fact]
    public async Task Content_change_recovers_after_embeddings_recover_and_is_idempotent()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("it-recover", body: "# One\n\nOriginal body.\n"));

        var embeddings = new FakeEmbeddingClient();
        await using var store = fixture.CreateIngestionStore();
        var service = new IngestionService(store, embeddings, new IngestionOptions(temp.Path, 1_048_576, 6000));
        await service.RunAsync(CancellationToken.None);

        File.WriteAllText(path, MarkdownFixtures.Policy("it-recover", body: "# Two\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(CancellationToken.None));

        embeddings.FailOnTexts.Clear();
        await service.RunAsync(CancellationToken.None);

        await using var retrieval = fixture.CreateRetrievalStore();
        var source = await retrieval.GetSourceDocumentAsync("it-recover", [AccessPolicies.Employee], CancellationToken.None);
        Assert.NotNull(source);
        Assert.Equal("# Two\n\nChanged body.", source.Content);

        var versions = await fixture.CountVersionsAsync("it-recover");
        await service.RunAsync(CancellationToken.None);
        Assert.Equal(versions, await fixture.CountVersionsAsync("it-recover"));
    }

    private static async Task<bool> IsSearchableAsync(
        NpgsqlRetrievalStore retrieval,
        string documentId,
        string body,
        IReadOnlyCollection<string> policies)
    {
        var results = await retrieval.SearchAsync(
            FakeEmbeddingClient.VectorFor(body), policies, 10, CancellationToken.None);
        return results.Any(chunk => chunk.DocumentId == documentId);
    }
}
