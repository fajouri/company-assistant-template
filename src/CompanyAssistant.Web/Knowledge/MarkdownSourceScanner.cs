namespace CompanyAssistant.Web.Knowledge;

internal static class SourcePathSafety
{
    // Returns the safe POSIX relative path when candidateFullPath is inside rootFullPath,
    // otherwise null. Both inputs must be fully qualified paths. Rejects path traversal
    // (".." segments) and paths that resolve outside the configured read-only root.
    public static string? GetSafeRelativePath(string rootFullPath, string candidateFullPath)
    {
        var relative = Path.GetRelativePath(rootFullPath, candidateFullPath);
        if (relative == "."
            || relative == ".."
            || Path.IsPathRooted(relative)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            return null;

        var normalized = relative
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        if (normalized.StartsWith("../", StringComparison.Ordinal) || normalized == "..")
            return null;

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == ".." || segment.Contains('\\'))
                return null;
        }

        return normalized;
    }
}

internal sealed record MarkdownFile(string RelativePath, string FullPath);

internal sealed record MarkdownScanResult(IReadOnlyList<MarkdownFile> Files, int Rejected);

// Enumerates Markdown sources under a read-only root, rejecting path traversal and
// symlink escapes. The real-path resolver is injectable so deterministic tests can
// simulate a symlink whose final target lies outside the root.
internal sealed class MarkdownSourceScanner
{
    private readonly Func<string, string> _resolveRealPath;

    public MarkdownSourceScanner(Func<string, string>? resolveRealPath = null) =>
        _resolveRealPath = resolveRealPath ?? ResolveRealPath;

    public MarkdownScanResult Scan(string root)
    {
        var rootFull = Path.GetFullPath(root);
        if (!Directory.Exists(rootFull))
            throw new InvalidOperationException($"Ingestion source folder does not exist: {rootFull}");

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            // Never follow reparse points (symlinks/junctions) while recursing.
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        var files = new List<MarkdownFile>();
        var rejected = 0;

        foreach (var path in Directory.EnumerateFiles(rootFull, "*.md", enumeration))
        {
            string resolved;
            try
            {
                resolved = _resolveRealPath(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                rejected++;
                continue;
            }

            var relative = SourcePathSafety.GetSafeRelativePath(rootFull, resolved);
            if (relative is null)
            {
                rejected++;
                continue;
            }

            files.Add(new MarkdownFile(relative, path));
        }

        return new MarkdownScanResult(files, rejected);
    }

    private static string ResolveRealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var target = File.ResolveLinkTarget(full, returnFinalTarget: true);
        return target?.FullName ?? full;
    }
}
