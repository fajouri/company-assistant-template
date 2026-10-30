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
        var report = await service.RunAsync(CancellationToken.None);
        Assert.Equal(1, report.Pending);

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
        var report = await service.RunAsync(CancellationToken.None);
        Assert.Equal(1, report.Pending);

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

    [Fact]
    public async Task Revocation_with_content_change_hides_previous_version_when_embeddings_fail()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("it-revoke-content", body: "# Revoke\n\nRevoke body.\n"));

        var embeddings = new FakeEmbeddingClient();
        await using var store = fixture.CreateIngestionStore();
        var service = new IngestionService(store, embeddings, new IngestionOptions(temp.Path, 1_048_576, 6000));
        await service.RunAsync(CancellationToken.None);

        await using var retrieval = fixture.CreateRetrievalStore();
        Assert.True(await IsSearchableAsync(retrieval, "it-revoke-content", "# Revoke\n\nRevoke body.", [AccessPolicies.Employee]));

        File.WriteAllText(path, MarkdownFixtures.Policy("it-revoke-content", approvalState: "revoked", body: "# Revoke\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Pending);
        Assert.False(await IsSearchableAsync(retrieval, "it-revoke-content", "# Revoke\n\nRevoke body.", [AccessPolicies.Employee]));
        Assert.Null(await retrieval.GetSourceDocumentAsync("it-revoke-content", [AccessPolicies.Employee], CancellationToken.None));
    }

    [Fact]
    public async Task Access_restriction_with_content_change_removes_employee_access_when_embeddings_fail()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("it-restrict-content", body: "# Restrict\n\nRestrict body.\n"));

        var embeddings = new FakeEmbeddingClient();
        await using var store = fixture.CreateIngestionStore();
        var service = new IngestionService(store, embeddings, new IngestionOptions(temp.Path, 1_048_576, 6000));
        await service.RunAsync(CancellationToken.None);

        await using var retrieval = fixture.CreateRetrievalStore();
        Assert.True(await IsSearchableAsync(retrieval, "it-restrict-content", "# Restrict\n\nRestrict body.", [AccessPolicies.Employee]));

        File.WriteAllText(path, MarkdownFixtures.Policy("it-restrict-content", accessPolicy: "hr", body: "# Restrict\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Pending);
        Assert.False(await IsSearchableAsync(retrieval, "it-restrict-content", "# Restrict\n\nRestrict body.", [AccessPolicies.Employee]));
        Assert.Null(await retrieval.GetSourceDocumentAsync("it-restrict-content", [AccessPolicies.Employee], CancellationToken.None));
        // The previous content must not be exposed to the newly granted audience either.
        Assert.False(await IsSearchableAsync(retrieval, "it-restrict-content", "# Restrict\n\nRestrict body.", [AccessPolicies.Hr]));
    }

    [Fact]
    public async Task Embedding_failure_does_not_block_restrictions_of_other_documents()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a-failing.md"), MarkdownFixtures.Policy("it-failing", body: "# Failing\n\nOriginal failing body.\n"));
        File.WriteAllText(Path.Combine(temp.Path, "b-restrict.md"), MarkdownFixtures.Policy("it-restrict", body: "# Restrict\n\nOriginal restrict body.\n"));

        var embeddings = new FakeEmbeddingClient();
        await using var store = fixture.CreateIngestionStore();
        var service = new IngestionService(store, embeddings, new IngestionOptions(temp.Path, 1_048_576, 6000));
        await service.RunAsync(CancellationToken.None);

        await using var retrieval = fixture.CreateRetrievalStore();
        Assert.True(await IsSearchableAsync(retrieval, "it-restrict", "# Restrict\n\nOriginal restrict body.", [AccessPolicies.Employee]));

        // Both documents change content; both embeddings fail. The revocation of the second
        // document must still be applied even though the first document cannot be indexed.
        File.WriteAllText(Path.Combine(temp.Path, "a-failing.md"), MarkdownFixtures.Policy("it-failing", body: "# Failing\n\nChanged failing body.\n"));
        File.WriteAllText(Path.Combine(temp.Path, "b-restrict.md"), MarkdownFixtures.Policy("it-restrict", approvalState: "revoked", body: "# Restrict\n\nChanged restrict body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, report.Pending);
        Assert.False(await IsSearchableAsync(retrieval, "it-restrict", "# Restrict\n\nOriginal restrict body.", [AccessPolicies.Employee]));
        Assert.Null(await retrieval.GetSourceDocumentAsync("it-restrict", [AccessPolicies.Employee], CancellationToken.None));
    }

    [Fact]
    public async Task Unconfigured_embeddings_apply_restrictions_and_report_pending_content()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("it-noconfig", body: "# Original\n\nOriginal body.\n"));

        await using var store = fixture.CreateIngestionStore();
        await new IngestionService(store, new FakeEmbeddingClient(), new IngestionOptions(temp.Path, 1_048_576, 6000))
            .RunAsync(CancellationToken.None);

        await using var retrieval = fixture.CreateRetrievalStore();
        Assert.True(await IsSearchableAsync(retrieval, "it-noconfig", "# Original\n\nOriginal body.", [AccessPolicies.Employee]));

        // Revoke the existing document (content unchanged) and add a new document that needs
        // embeddings. With embeddings unconfigured, the revocation must still be applied and the
        // new document must remain pending.
        File.WriteAllText(path, MarkdownFixtures.Policy("it-noconfig", approvalState: "revoked", body: "# Original\n\nOriginal body.\n"));
        File.WriteAllText(Path.Combine(temp.Path, "new.md"), MarkdownFixtures.Policy("it-new", body: "# New\n\nNew body.\n"));

        var report = await new IngestionService(store, new FakeEmbeddingClient(isConfigured: false), new IngestionOptions(temp.Path, 1_048_576, 6000))
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Pending);
        Assert.True(report.HasFailures);
        Assert.False(await IsSearchableAsync(retrieval, "it-noconfig", "# Original\n\nOriginal body.", [AccessPolicies.Employee]));
        Assert.Null(await retrieval.GetSourceDocumentAsync("it-new", [AccessPolicies.Employee], CancellationToken.None));
    }

    [Fact]
    public async Task Access_widening_with_content_change_does_not_expose_previous_content()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("it-widen", accessPolicy: "hr", body: "# Hr\n\nHR only body.\n"));

        var embeddings = new FakeEmbeddingClient();
        await using var store = fixture.CreateIngestionStore();
        var service = new IngestionService(store, embeddings, new IngestionOptions(temp.Path, 1_048_576, 6000));
        await service.RunAsync(CancellationToken.None);

        await using var retrieval = fixture.CreateRetrievalStore();
        Assert.False(await IsSearchableAsync(retrieval, "it-widen", "# Hr\n\nHR only body.", [AccessPolicies.Employee]));

        File.WriteAllText(path, MarkdownFixtures.Policy("it-widen", accessPolicy: "employee", body: "# Hr\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Pending);
        Assert.False(await IsSearchableAsync(retrieval, "it-widen", "# Hr\n\nHR only body.", [AccessPolicies.Employee]));
        Assert.Null(await retrieval.GetSourceDocumentAsync("it-widen", [AccessPolicies.Employee], CancellationToken.None));
    }

    [Fact]
    public async Task Restriction_with_content_change_recovers_and_stays_idempotent()
    {
        await fixture.ResetAsync();
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("it-recover-restrict", body: "# One\n\nOriginal body.\n"));

        var embeddings = new FakeEmbeddingClient();
        await using var store = fixture.CreateIngestionStore();
        var service = new IngestionService(store, embeddings, new IngestionOptions(temp.Path, 1_048_576, 6000));
        await service.RunAsync(CancellationToken.None);
        var versionsBefore = await fixture.CountVersionsAsync("it-recover-restrict");

        File.WriteAllText(path, MarkdownFixtures.Policy("it-recover-restrict", approvalState: "revoked", body: "# Two\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var first = await service.RunAsync(CancellationToken.None);
        Assert.Equal(1, first.Pending);

        await using (var retrieval = fixture.CreateRetrievalStore())
            Assert.Null(await retrieval.GetSourceDocumentAsync("it-recover-restrict", [AccessPolicies.Employee], CancellationToken.None));

        embeddings.FailOnTexts.Clear();
        await service.RunAsync(CancellationToken.None);
        var versionsAfterRecovery = await fixture.CountVersionsAsync("it-recover-restrict");
        Assert.Equal(versionsBefore + 1, versionsAfterRecovery);

        await service.RunAsync(CancellationToken.None);
        Assert.Equal(versionsAfterRecovery, await fixture.CountVersionsAsync("it-recover-restrict"));
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
