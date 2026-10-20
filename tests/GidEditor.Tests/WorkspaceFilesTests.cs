using System.IO;
using GidEditor.Services;
using Xunit;

namespace GidEditor.Tests;

public class WorkspaceFilesTests
{
    [Fact]
    public void Relative_link_uses_forward_slashes_and_escapes_spaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "ws");
        var note = Path.Combine(root, "Projects", "Plan.md");
        var image = Path.Combine(root, "attachments", "my shot.png");
        Assert.Equal("../attachments/my%20shot.png", WorkspaceFiles.RelativeLink(note, image));
    }

    [Theory]
    [InlineData("a/b:c?", "abc")]
    [InlineData("  name.  ", "name")]
    [InlineData("///", "Untitled")]
    public void Sanitize_removes_characters_invalid_on_any_os(string input, string expected) =>
        Assert.Equal(expected, WorkspaceFiles.SanitizeFileName(input));

    [Fact]
    public void Hidden_and_sync_folders_are_ignored()
    {
        var root = Path.Combine(Path.GetTempPath(), "ws");
        Assert.True(WorkspaceFiles.IsInIgnoredLocation(root, Path.Combine(root, ".git", "HEAD")));
        Assert.True(WorkspaceFiles.IsInIgnoredLocation(root, Path.Combine(root, "~$note.md")));
        Assert.False(WorkspaceFiles.IsInIgnoredLocation(root, Path.Combine(root, "notes", "a.md")));
    }
}
