using System.Security.Cryptography;
using System.Text;

namespace CompanyAssistant.Web.Knowledge;

internal static class ApprovalStates
{
    public const string Draft = "draft";
    public const string Approved = "approved";
    public const string Revoked = "revoked";

    public static readonly IReadOnlyList<string> All = [Draft, Approved, Revoked];
}

internal static class AccessPolicies
{
    public const string Employee = "employee";
    public const string Hr = "hr";

    public static readonly IReadOnlyList<string> All = [Employee, Hr];
}

internal static class Hashing
{
    public static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}

internal sealed record PolicyMetadata(
    string DocumentId,
    string Title,
    string Owner,
    string? Approver,
    string Version,
    DateOnly EffectiveDate,
    DateOnly? ReviewDate,
    string ApprovalState,
    string AccessPolicy);

internal sealed record MarkdownChunk(string? SectionHeading, string Content);

internal sealed record ParsedDocument(
    PolicyMetadata Metadata,
    string Content,
    string ContentSha256,
    IReadOnlyList<MarkdownChunk> Chunks);

internal sealed record IngestedChunk(
    string? SectionHeading,
    string Content,
    string ContentSha256,
    float[] Embedding);

internal sealed record IngestedDocument(
    PolicyMetadata Metadata,
    string SourcePath,
    string Content,
    string ContentSha256,
    IReadOnlyList<IngestedChunk> Chunks);

internal sealed record StoredDocument(
    string DocumentId,
    string SourcePath,
    string ContentSha256,
    string Content,
    string Title,
    string Owner,
    string? Approver,
    string Version,
    DateOnly EffectiveDate,
    DateOnly? ReviewDate,
    string ApprovalState,
    string AccessPolicy,
    DateTimeOffset? DeletedAt);

internal sealed record RetrievedChunk(
    long ChunkId,
    string DocumentId,
    string Title,
    string Version,
    int ChunkIndex,
    string? SectionHeading,
    string Content,
    double Distance);

internal sealed record SourceDocument(
    string DocumentId,
    string Title,
    string Version,
    string AccessPolicy,
    DateOnly EffectiveDate,
    string ApprovalState,
    string SourcePath,
    string Content);

internal sealed record RetrievalCaller(IReadOnlyCollection<string> Permissions);

internal sealed record IngestionReport(
    int Indexed,
    int Unchanged,
    int Deleted,
    int SkippedUnsafe,
    int Oversized,
    int Failed,
    int Pending)
{
    public bool HasFailures => SkippedUnsafe > 0 || Oversized > 0 || Failed > 0 || Pending > 0;
}
