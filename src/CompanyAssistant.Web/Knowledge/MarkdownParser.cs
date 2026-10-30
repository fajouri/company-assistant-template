using System.Text;

namespace CompanyAssistant.Web.Knowledge;

internal static class MarkdownParser
{
    public static ParsedDocument Parse(byte[] bytes, int maxChunkChars)
    {
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var (metadata, body) = SplitFrontMatter(text);
        var chunks = MarkdownChunker.Chunk(body, maxChunkChars);
        return new ParsedDocument(metadata, body, Hashing.Sha256Hex(bytes), chunks);
    }

    private static (PolicyMetadata Metadata, string Body) SplitFrontMatter(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
            throw new FormatException("Markdown policy must start with a '---' front-matter fence.");

        var end = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                end = i;
                break;
            }
        }

        if (end < 0)
            throw new FormatException("Markdown policy front-matter is missing its closing '---' fence.");

        var metadata = ParseMetadata(lines.Skip(1).Take(end - 1));
        var body = string.Join('\n', lines.Skip(end + 1)).TrimStart('\n').TrimEnd();
        return (metadata, body);
    }

    private static PolicyMetadata ParseMetadata(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var colon = line.IndexOf(':');
            if (colon <= 0)
                throw new FormatException($"Invalid front-matter line: '{raw}'.");

            var key = line[..colon].Trim();
            var value = Unquote(line[(colon + 1)..].Trim());
            if (values.ContainsKey(key))
                throw new FormatException($"Duplicate front-matter key: '{key}'.");
            values[key] = value;
        }

        string[] required = ["id", "title", "owner", "version", "effective_date", "approval_state", "access_policy"];
        foreach (var key in required)
        {
            if (!values.ContainsKey(key))
                throw new FormatException($"Missing required front-matter key: '{key}'.");
        }

        string[] known = ["id", "title", "owner", "approver", "version", "effective_date", "review_date", "approval_state", "access_policy"];
        foreach (var key in values.Keys)
        {
            if (!known.Contains(key, StringComparer.OrdinalIgnoreCase))
                throw new FormatException($"Unknown front-matter key: '{key}'.");
        }

        var id = Require(values, "id");
        if (id.Length > 128 || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new FormatException("Front-matter 'id' must be 1-128 characters of letters, digits, '-', '_' or '.'.");

        var title = Bound(Require(values, "title"), 256, "title");
        var owner = Bound(Require(values, "owner"), 128, "owner");
        var approver = values.TryGetValue("approver", out var ap) && ap.Length > 0 ? Bound(ap, 128, "approver") : null;
        var version = Bound(Require(values, "version"), 64, "version");

        var effectiveDate = ParseDate(Require(values, "effective_date"), "effective_date");
        var reviewDate = values.TryGetValue("review_date", out var rd) && rd.Length > 0
            ? ParseDate(rd, "review_date")
            : (DateOnly?)null;

        var approvalState = ParseEnum(Require(values, "approval_state"), ApprovalStates.All, "approval_state");
        var accessPolicy = ParseEnum(Require(values, "access_policy"), AccessPolicies.All, "access_policy");

        return new PolicyMetadata(id, title, owner, approver, version, effectiveDate, reviewDate, approvalState, accessPolicy);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    private static string Require(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && value.Length > 0
            ? value
            : throw new FormatException($"Front-matter '{key}' must not be empty.");

    private static string Bound(string value, int max, string key) =>
        value.Length <= max
            ? value
            : throw new FormatException($"Front-matter '{key}' must be at most {max} characters.");

    private static DateOnly ParseDate(string value, string key) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date)
            ? date
            : throw new FormatException($"Front-matter '{key}' must use the yyyy-MM-dd date format.");

    private static string ParseEnum(string value, IReadOnlyList<string> allowed, string key) =>
        allowed.FirstOrDefault(candidate => candidate.Equals(value, StringComparison.OrdinalIgnoreCase))
        ?? throw new FormatException($"Front-matter '{key}' must be one of {string.Join(", ", allowed)}.");
}
