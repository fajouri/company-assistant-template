using CompanyAssistant.Web.Knowledge;

namespace CompanyAssistant.Web.Tests;

public sealed class IngestionSafetyTests
{
    [Fact]
    public void Relative_paths_outside_the_root_are_rejected()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "root"));
        var outside = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "outside", "file.md"));
        var traversal = Path.GetFullPath(Path.Combine(root, "..", "outside", "file.md"));

        Assert.Null(SourcePathSafety.GetSafeRelativePath(root, outside));
        Assert.Null(SourcePathSafety.GetSafeRelativePath(root, traversal));
    }

    [Fact]
    public void Files_inside_the_root_get_a_safe_relative_path()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "root"));
        var file = Path.GetFullPath(Path.Combine(root, "policies", "leave.md"));

        var relative = SourcePathSafety.GetSafeRelativePath(root, file);

        Assert.Equal("policies/leave.md", relative);
    }

    [Fact]
    public void Scanner_rejects_a_symlink_whose_target_is_outside_the_root()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "root");
        var outside = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.md"), MarkdownFixtures.Policy("secret"));

        var planted = Path.Combine(root, "escape.md");
        File.WriteAllText(planted, MarkdownFixtures.Policy("escape"));

        // Simulate the file being a symlink whose final target resolves outside the root.
        var scanner = new MarkdownSourceScanner(path => path == Path.GetFullPath(planted)
            ? Path.GetFullPath(Path.Combine(outside, "secret.md"))
            : Path.GetFullPath(path));

        var result = scanner.Scan(root);

        Assert.Empty(result.Files);
        Assert.Equal(1, result.Rejected);
    }

    [Fact]
    public void Scanner_only_reads_markdown_files()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "policy.md"), MarkdownFixtures.Policy("policy"));
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "not markdown");

        var result = new MarkdownSourceScanner().Scan(temp.Path);

        Assert.Single(result.Files);
        Assert.Equal("policy.md", result.Files[0].RelativePath);
    }

    [Fact]
    public void Scanner_rejects_missing_source_folder()
    {
        Assert.Throws<InvalidOperationException>(
            () => new MarkdownSourceScanner().Scan(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "company-assistant-tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
