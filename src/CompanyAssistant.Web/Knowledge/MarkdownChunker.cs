using System.Text;

namespace CompanyAssistant.Web.Knowledge;

internal static class MarkdownChunker
{
    public static IReadOnlyList<MarkdownChunk> Chunk(string body, int maxChunkChars)
    {
        var chunks = new List<MarkdownChunk>();
        foreach (var (heading, text) in SplitSections(body))
        {
            foreach (var chunk in ChunkSection(heading, text, maxChunkChars))
                chunks.Add(chunk);
        }

        return chunks;
    }

    private static List<(string? Heading, string Text)> SplitSections(string body)
    {
        var sections = new List<(string?, string)>();
        string? heading = null;
        var buffer = new StringBuilder();

        foreach (var line in body.Split('\n'))
        {
            var clean = line.TrimEnd('\r');
            if (TryReadHeading(clean, out var nextHeading))
            {
                if (buffer.Length > 0 || heading is not null)
                    sections.Add((heading, buffer.ToString()));
                heading = nextHeading;
                buffer.Clear();
            }

            buffer.Append(clean);
            buffer.Append('\n');
        }

        if (buffer.Length > 0 || heading is not null)
            sections.Add((heading, buffer.ToString()));

        return sections;
    }

    private static bool TryReadHeading(string line, out string heading)
    {
        heading = string.Empty;
        var trimmed = line.TrimStart();
        var level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
            level++;

        if (level == 0 || level > 6 || level >= trimmed.Length || trimmed[level] != ' ')
            return false;

        heading = trimmed[level..].Trim();
        return true;
    }

    private static IEnumerable<MarkdownChunk> ChunkSection(string? heading, string text, int maxChunkChars)
    {
        var buffer = new StringBuilder();
        foreach (var paragraph in SplitParagraphs(text))
        {
            if (paragraph.Length > maxChunkChars)
            {
                if (buffer.Length > 0)
                {
                    yield return new MarkdownChunk(heading, buffer.ToString().Trim());
                    buffer.Clear();
                }

                foreach (var piece in HardSplit(paragraph, maxChunkChars))
                    yield return new MarkdownChunk(heading, piece);
                continue;
            }

            var appendedLength = buffer.Length == 0 ? paragraph.Length : buffer.Length + 2 + paragraph.Length;
            if (appendedLength > maxChunkChars)
            {
                yield return new MarkdownChunk(heading, buffer.ToString().Trim());
                buffer.Clear();
                buffer.Append(paragraph);
            }
            else
            {
                if (buffer.Length > 0)
                    buffer.Append("\n\n");
                buffer.Append(paragraph);
            }
        }

        if (buffer.Length > 0)
            yield return new MarkdownChunk(heading, buffer.ToString().Trim());
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        foreach (var part in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim('\n');
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

    private static IEnumerable<string> HardSplit(string text, int max)
    {
        for (var i = 0; i < text.Length; i += max)
            yield return text.Substring(i, Math.Min(max, text.Length - i));
    }
}
