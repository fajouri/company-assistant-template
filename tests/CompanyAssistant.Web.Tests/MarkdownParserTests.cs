using System.Text;
using CompanyAssistant.Web.Knowledge;

namespace CompanyAssistant.Web.Tests;

public sealed class MarkdownParserTests
{
    [Fact]
    public void Valid_front_matter_parses_metadata()
    {
        var bytes = Encoding.UTF8.GetBytes(MarkdownFixtures.Policy("leave-policy"));

        var parsed = MarkdownParser.Parse(bytes, maxChunkChars: 6000);

        Assert.Equal("leave-policy", parsed.Metadata.DocumentId);
        Assert.Equal("leave-policy policy", parsed.Metadata.Title);
        Assert.Equal("Fictional Owner", parsed.Metadata.Owner);
        Assert.Equal("Fictional Approver", parsed.Metadata.Approver);
        Assert.Equal("1.0", parsed.Metadata.Version);
        Assert.Equal(new DateOnly(2025, 1, 1), parsed.Metadata.EffectiveDate);
        Assert.Equal(new DateOnly(2026, 6, 30), parsed.Metadata.ReviewDate);
        Assert.Equal(ApprovalStates.Approved, parsed.Metadata.ApprovalState);
        Assert.Equal(AccessPolicies.Employee, parsed.Metadata.AccessPolicy);
        Assert.Single(parsed.Chunks);
        Assert.Equal("Overview", parsed.Chunks[0].SectionHeading);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("title")]
    [InlineData("owner")]
    [InlineData("version")]
    [InlineData("effective_date")]
    [InlineData("approval_state")]
    [InlineData("access_policy")]
    public void Missing_required_key_is_rejected(string missingKey)
    {
        var markdown = MarkdownFixtures.Policy("doc").Replace($"\n{missingKey}:", "\n__remove__:");
        Assert.Throws<FormatException>(() => MarkdownParser.Parse(Encoding.UTF8.GetBytes(markdown), 6000));
    }

    [Fact]
    public void Unknown_front_matter_key_is_rejected()
    {
        var markdown = MarkdownFixtures.Policy("doc").Replace("review_date: 2026-06-30", "secret_note: hi");
        Assert.Throws<FormatException>(() => MarkdownParser.Parse(Encoding.UTF8.GetBytes(markdown), 6000));
    }

    [Theory]
    [InlineData("approval_state: pending")]
    [InlineData("access_policy: admin")]
    [InlineData("effective_date: 01/01/2025")]
    [InlineData("id: not valid id!")]
    public void Invalid_metadata_values_are_rejected(string replacement)
    {
        var markdown = MarkdownFixtures.Policy("doc");
        var target = replacement.Split(':')[0];
        markdown = markdown.Replace($"{target}:", $"__remove__:");
        markdown = markdown.Replace("__remove__:", replacement);

        Assert.Throws<FormatException>(() => MarkdownParser.Parse(Encoding.UTF8.GetBytes(markdown), 6000));
    }

    [Fact]
    public void Chunker_retains_sections_and_bounds_chunk_size()
    {
        var body = "# One\n\n" + new string('a', 100) + "\n\n# Two\n\n" + new string('b', 100) + "\n";

        var chunks = MarkdownChunker.Chunk(body, maxChunkChars: 60);

        Assert.True(chunks.Count >= 3);
        Assert.Contains(chunks, chunk => chunk.SectionHeading == "One");
        Assert.Contains(chunks, chunk => chunk.SectionHeading == "Two");
        Assert.All(chunks, chunk => Assert.True(chunk.Content.Length <= 61));
    }

    [Fact]
    public void Content_hash_is_stable_for_identical_bytes()
    {
        var bytes = Encoding.UTF8.GetBytes(MarkdownFixtures.Policy("doc"));

        var first = MarkdownParser.Parse(bytes, 6000).ContentSha256;
        var second = MarkdownParser.Parse(Encoding.UTF8.GetBytes(MarkdownFixtures.Policy("doc")), 6000).ContentSha256;

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }
}
