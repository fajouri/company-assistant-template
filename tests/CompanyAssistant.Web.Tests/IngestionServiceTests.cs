using System.Text;
using CompanyAssistant.Web.Knowledge;

namespace CompanyAssistant.Web.Tests;

public sealed class IngestionServiceTests
{
    private static IngestionService CreateService(
        FakeIngestionStore store,
        FakeEmbeddingClient embeddings,
        string sourcePath) =>
        new(store, embeddings, new IngestionOptions(sourcePath, 1_048_576, 6000));

    [Fact]
    public async Task Repeated_ingestion_is_idempotent()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.md"), MarkdownFixtures.Policy("a"));
        File.WriteAllText(Path.Combine(temp.Path, "b.md"), MarkdownFixtures.Policy("b"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);

        var first = await service.RunAsync(CancellationToken.None);
        var second = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, first.Indexed);
        Assert.Equal(0, first.Unchanged);
        Assert.Equal(2, second.Unchanged);
        Assert.Equal(0, second.Indexed);
        Assert.Empty(store.Upserts.Skip(2));
        Assert.Equal(2, store.Upserts.Count);
    }

    [Fact]
    public async Task Changed_document_is_reindexed_without_duplicates()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a"));

        var store = new FakeIngestionStore();
        var service = CreateService(store, new FakeEmbeddingClient(), temp.Path);
        await service.RunAsync(CancellationToken.None);
        var originalHash = store.Documents["a"].ContentSha256;

        File.WriteAllText(path, MarkdownFixtures.Policy("a", body: "# Changed\n\nNew content.\n"));
        await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, store.Upserts.Count);
        Assert.Single(store.Documents);
        Assert.NotEqual(originalHash, store.Documents["a"].ContentSha256);
    }

    [Fact]
    public async Task Removed_document_is_soft_deleted()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a"));

        var store = new FakeIngestionStore();
        var service = CreateService(store, new FakeEmbeddingClient(), temp.Path);
        await service.RunAsync(CancellationToken.None);
        Assert.Null(store.Documents["a"].DeletedAt);

        File.Delete(path);
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Deleted);
        Assert.NotNull(store.Documents["a"].DeletedAt);
    }

    [Fact]
    public async Task Revoked_document_is_reindexed_as_revoked()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a"));

        var store = new FakeIngestionStore();
        var service = CreateService(store, new FakeEmbeddingClient(), temp.Path);
        await service.RunAsync(CancellationToken.None);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", approvalState: "revoked"));
        await service.RunAsync(CancellationToken.None);

        Assert.Equal(ApprovalStates.Revoked, store.Documents["a"].ApprovalState);
    }

    [Fact]
    public async Task Future_effective_document_is_stored_but_marked_future()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.md"),
            MarkdownFixtures.Policy("a", effectiveDate: "2999-01-01"));

        var store = new FakeIngestionStore();
        var service = CreateService(store, new FakeEmbeddingClient(), temp.Path);
        await service.RunAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2999, 1, 1), store.Documents["a"].EffectiveDate);
    }

    [Fact]
    public async Task Embedding_failure_does_not_partially_write_the_failed_document()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.md"), MarkdownFixtures.Policy("a", body: "# One\n\nAlpha body.\n"));
        File.WriteAllText(Path.Combine(temp.Path, "b.md"), MarkdownFixtures.Policy("b", body: "# Two\n\nBeta body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        embeddings.FailOnTexts.Add("Beta");
        var service = CreateService(store, embeddings, temp.Path);

        var report = await service.RunAsync(CancellationToken.None);

        Assert.DoesNotContain(store.Upserts, upsert => upsert.Metadata.DocumentId == "b");
        Assert.Contains(store.Upserts, upsert => upsert.Metadata.DocumentId == "a");
        Assert.Empty(store.RetireCalls);
        Assert.Null(store.Documents["a"].DeletedAt);
        Assert.Equal(1, report.Pending);
        Assert.True(report.HasFailures);
    }

    [Fact]
    public async Task Oversized_file_is_skipped_and_not_deleted()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.md"), new string('x', 200));

        var store = new FakeIngestionStore();
        var service = new IngestionService(store, new FakeEmbeddingClient(), new IngestionOptions(temp.Path, 100, 6000));
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, report.Oversized);
        Assert.Empty(store.Upserts);
        Assert.Equal(0, report.Deleted);
    }

    [Fact]
    public async Task Revocation_is_applied_without_embeddings_when_provider_fails()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);
        Assert.Equal(ApprovalStates.Approved, store.Documents["a"].ApprovalState);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", approvalState: "revoked"));
        embeddings.FailOnTexts.Add("Fictional");
        await service.RunAsync(CancellationToken.None);

        Assert.Equal(ApprovalStates.Revoked, store.Documents["a"].ApprovalState);
        Assert.Equal(1, embeddings.EmbedCallCount);
        Assert.Empty(store.Upserts.Skip(1));
        Assert.Single(store.MetadataUpdates);
    }

    [Fact]
    public async Task Access_restriction_is_applied_without_embeddings_when_provider_fails()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a", accessPolicy: "employee"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", accessPolicy: "hr"));
        embeddings.FailOnTexts.Add("Fictional");
        await service.RunAsync(CancellationToken.None);

        Assert.Equal(AccessPolicies.Hr, store.Documents["a"].AccessPolicy);
        Assert.Equal(1, embeddings.EmbedCallCount);
    }

    [Fact]
    public async Task Content_change_does_not_publish_when_embeddings_fail()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a", body: "# One\n\nOriginal body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);
        var original = store.Documents["a"];

        File.WriteAllText(path, MarkdownFixtures.Policy("a", body: "# Two\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");

        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(original.Content, store.Documents["a"].Content);
        Assert.Equal(original.ContentSha256, store.Documents["a"].ContentSha256);
        Assert.Single(store.Upserts);
        Assert.Empty(store.MetadataUpdates);
        Assert.Empty(store.RetireCalls);
        Assert.Equal(1, report.Pending);
    }

    [Fact]
    public async Task Content_change_recovers_after_embeddings_recover_and_is_idempotent()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a", body: "# One\n\nOriginal body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", body: "# Two\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);
        Assert.Equal(1, report.Pending);

        embeddings.FailOnTexts.Clear();
        await service.RunAsync(CancellationToken.None);
        Assert.Equal("# Two\n\nChanged body.", store.Documents["a"].Content);

        var upserts = store.Upserts.Count;
        await service.RunAsync(CancellationToken.None);
        Assert.Equal(upserts, store.Upserts.Count);
        Assert.Single(store.Documents);
    }

    [Fact]
    public async Task Revocation_with_content_change_retires_previous_version_when_embeddings_fail()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a", body: "# One\n\nOriginal body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);
        Assert.Equal(ApprovalStates.Approved, store.Documents["a"].ApprovalState);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", approvalState: "revoked", body: "# Two\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Contains("a", store.RetireCalls);
        Assert.NotNull(store.Documents["a"].DeletedAt);
        Assert.Equal(ApprovalStates.Approved, store.Documents["a"].ApprovalState);
        Assert.Equal("# One\n\nOriginal body.", store.Documents["a"].Content);
        Assert.Equal(1, report.Pending);
    }

    [Fact]
    public async Task Access_restriction_with_content_change_retires_previous_version_when_embeddings_fail()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a", accessPolicy: "employee", body: "# One\n\nOriginal body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", accessPolicy: "hr", body: "# Two\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Contains("a", store.RetireCalls);
        Assert.NotNull(store.Documents["a"].DeletedAt);
        Assert.Equal(AccessPolicies.Employee, store.Documents["a"].AccessPolicy);
        Assert.Equal(1, report.Pending);
    }

    [Fact]
    public async Task Embedding_failure_does_not_block_restrictions_of_other_documents()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a-failing.md"), MarkdownFixtures.Policy("a-failing", body: "# Failing\n\nOriginal failing body.\n"));
        File.WriteAllText(Path.Combine(temp.Path, "b-restrict.md"), MarkdownFixtures.Policy("b-restrict", body: "# Restrict\n\nOriginal restrict body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);

        File.WriteAllText(Path.Combine(temp.Path, "a-failing.md"), MarkdownFixtures.Policy("a-failing", body: "# Failing\n\nChanged failing body.\n"));
        File.WriteAllText(Path.Combine(temp.Path, "b-restrict.md"), MarkdownFixtures.Policy("b-restrict", approvalState: "revoked", body: "# Restrict\n\nChanged restrict body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, report.Pending);
        Assert.Contains("b-restrict", store.RetireCalls);
        Assert.NotNull(store.Documents["b-restrict"].DeletedAt);
        Assert.Null(store.Documents["a-failing"].DeletedAt);
    }

    [Fact]
    public async Task Unconfigured_embeddings_apply_restrictions_and_report_pending_content()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a"));

        var store = new FakeIngestionStore();
        await CreateService(store, new FakeEmbeddingClient(), temp.Path).RunAsync(CancellationToken.None);
        Assert.Equal(ApprovalStates.Approved, store.Documents["a"].ApprovalState);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", approvalState: "revoked"));
        File.WriteAllText(Path.Combine(temp.Path, "b.md"), MarkdownFixtures.Policy("b", body: "# New\n\nNew content.\n"));

        var report = await CreateService(store, new FakeEmbeddingClient(isConfigured: false), temp.Path)
            .RunAsync(CancellationToken.None);

        Assert.Equal(ApprovalStates.Revoked, store.Documents["a"].ApprovalState);
        Assert.DoesNotContain("b", store.Documents.Keys);
        Assert.Equal(1, report.Pending);
        Assert.True(report.HasFailures);
    }

    [Fact]
    public async Task Access_widening_with_content_change_does_not_expose_previous_content()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a", accessPolicy: "hr", body: "# Hr\n\nHR only body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", accessPolicy: "employee", body: "# Hr\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Contains("a", store.RetireCalls);
        Assert.NotNull(store.Documents["a"].DeletedAt);
        Assert.Equal(AccessPolicies.Hr, store.Documents["a"].AccessPolicy);
        Assert.Equal(1, report.Pending);
    }


    [Fact]
    public async Task Future_effective_date_change_with_content_change_retires_previous_version_when_embeddings_fail()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a", effectiveDate: "2025-01-01", body: "# One\n\nOriginal body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", effectiveDate: "2999-01-01", body: "# Two\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Contains("a", store.FutureEffectiveRetireCalls);
        Assert.NotNull(store.Documents["a"].DeletedAt);
        Assert.Equal(new DateOnly(2025, 1, 1), store.Documents["a"].EffectiveDate);
        Assert.Equal("# One\n\nOriginal body.", store.Documents["a"].Content);
        Assert.Equal(1, report.Pending);
    }

    [Fact]
    public async Task Unconfigured_embeddings_retire_previous_version_on_future_effective_date_change()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a", effectiveDate: "2025-01-01", body: "# One\n\nOriginal body.\n"));

        var store = new FakeIngestionStore();
        await CreateService(store, new FakeEmbeddingClient(), temp.Path).RunAsync(CancellationToken.None);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", effectiveDate: "2999-01-01", body: "# Two\n\nChanged body.\n"));
        var report = await CreateService(store, new FakeEmbeddingClient(isConfigured: false), temp.Path)
            .RunAsync(CancellationToken.None);

        Assert.Contains("a", store.FutureEffectiveRetireCalls);
        Assert.NotNull(store.Documents["a"].DeletedAt);
        Assert.Equal(1, report.Pending);
    }

    [Fact]
    public async Task Past_to_past_date_change_with_content_change_does_not_retire_when_embeddings_fail()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "a.md");
        File.WriteAllText(path, MarkdownFixtures.Policy("a", effectiveDate: "2025-01-01", body: "# One\n\nOriginal body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);
        await service.RunAsync(CancellationToken.None);

        File.WriteAllText(path, MarkdownFixtures.Policy("a", effectiveDate: "2025-02-01", body: "# Two\n\nChanged body.\n"));
        embeddings.FailOnTexts.Add("Changed");
        var report = await service.RunAsync(CancellationToken.None);

        Assert.Null(store.Documents["a"].DeletedAt);
        Assert.Equal(new DateOnly(2025, 1, 1), store.Documents["a"].EffectiveDate);
        Assert.Equal("# One\n\nOriginal body.", store.Documents["a"].Content);
        Assert.Equal(1, report.Pending);
    }

    [Fact]
    public async Task Duplicate_ids_are_rejected_before_any_write_or_embedding()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.md"), MarkdownFixtures.Policy("dup", body: "# One\n\nFirst body.\n"));
        File.WriteAllText(Path.Combine(temp.Path, "b.md"), MarkdownFixtures.Policy("dup", body: "# Two\n\nSecond body.\n"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(CancellationToken.None));

        Assert.Contains("id 'dup'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'a.md'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'b.md'", exception.Message, StringComparison.Ordinal);
        Assert.Empty(store.Upserts);
        Assert.Empty(store.MetadataUpdates);
        Assert.Empty(store.SoftDeleteCalls);
        Assert.Equal(0, embeddings.EmbedCallCount);
    }

    [Fact]
    public async Task Duplicate_ids_with_different_acl_are_rejected()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.md"), MarkdownFixtures.Policy("dup", accessPolicy: "employee"));
        File.WriteAllText(Path.Combine(temp.Path, "b.md"), MarkdownFixtures.Policy("dup", accessPolicy: "hr"));

        var store = new FakeIngestionStore();
        var embeddings = new FakeEmbeddingClient();
        var service = CreateService(store, embeddings, temp.Path);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(CancellationToken.None));

        Assert.Contains("id 'dup'", exception.Message, StringComparison.Ordinal);
        Assert.Empty(store.Upserts);
        Assert.Equal(0, embeddings.EmbedCallCount);
    }

    [Fact]
    public async Task Duplicate_ids_are_rejected_regardless_of_input_order()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "b.md"), MarkdownFixtures.Policy("dup", body: "# Two\n\nSecond body.\n"));
        File.WriteAllText(Path.Combine(temp.Path, "a.md"), MarkdownFixtures.Policy("dup", body: "# One\n\nFirst body.\n"));

        var store = new FakeIngestionStore();
        var service = CreateService(store, new FakeEmbeddingClient(), temp.Path);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(CancellationToken.None));

        Assert.Contains("id 'dup'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'a.md'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'b.md'", exception.Message, StringComparison.Ordinal);
        Assert.Empty(store.Upserts);
        Assert.Empty(store.SoftDeleteCalls);
    }
}
