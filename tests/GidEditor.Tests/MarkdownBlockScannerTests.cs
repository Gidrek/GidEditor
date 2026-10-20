using AvaloniaEdit.Document;
using GidEditor.Markdown;
using Xunit;

namespace GidEditor.Tests;

public class MarkdownBlockScannerTests
{
    [Fact]
    public void Detects_fenced_code_and_front_matter()
    {
        var doc = new TextDocument("---\ntitle: x\n---\ntext\n```js\ncode\n```\nafter");
        var scanner = new MarkdownBlockScanner(doc);

        Assert.Equal(LineKind.FrontMatter, scanner.GetKind(1));
        Assert.Equal(LineKind.FrontMatter, scanner.GetKind(3));
        Assert.Equal(LineKind.Normal, scanner.GetKind(4));
        Assert.Equal(LineKind.FenceDelimiter, scanner.GetKind(5));
        Assert.Equal(LineKind.Code, scanner.GetKind(6));
        Assert.Equal(LineKind.FenceDelimiter, scanner.GetKind(7));
        Assert.Equal(LineKind.Normal, scanner.GetKind(8));
    }

    [Fact]
    public void Unclosed_fence_runs_to_end_and_rescans_after_edits()
    {
        var doc = new TextDocument("```\na\nb");
        var scanner = new MarkdownBlockScanner(doc);
        Assert.Equal(LineKind.Code, scanner.GetKind(3));

        var raised = false;
        scanner.StructureChanged += (_, _) => raised = true;
        doc.Insert(doc.TextLength, "\n```\nc");
        Assert.Equal(LineKind.Normal, scanner.GetKind(5));
        Assert.True(raised);
    }

    [Fact]
    public void Tilde_fence_is_not_closed_by_backticks()
    {
        var doc = new TextDocument("~~~\n```\n~~~\nx");
        var scanner = new MarkdownBlockScanner(doc);
        Assert.Equal(LineKind.Code, scanner.GetKind(2));
        Assert.Equal(LineKind.Normal, scanner.GetKind(4));
    }
}
