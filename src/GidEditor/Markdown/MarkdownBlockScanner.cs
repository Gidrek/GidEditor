using System;
using AvaloniaEdit.Document;

namespace GidEditor.Markdown;

public enum LineKind : byte
{
    Normal,
    FenceDelimiter,
    Code,
    FrontMatter,
}

/// <summary>
/// Tracks multi-line constructs (fenced code blocks and YAML front matter) that a per-line colorizer
/// cannot detect on its own. Rescans lazily whenever the document version changes.
/// </summary>
public sealed class MarkdownBlockScanner
{
    private readonly TextDocument _document;
    private ITextSourceVersion? _version;
    private LineKind[] _kinds = [];

    public MarkdownBlockScanner(TextDocument document) => _document = document;

    /// <summary>Raised after a rescan changed the kind of any line, meaning the whole view must be redrawn.</summary>
    public event EventHandler? StructureChanged;

    public LineKind GetKind(int lineNumber)
    {
        EnsureScanned();
        var i = lineNumber - 1;
        return i >= 0 && i < _kinds.Length ? _kinds[i] : LineKind.Normal;
    }

    public void EnsureScanned()
    {
        var version = _document.Version;
        if (_version != null && version != null && _version.BelongsToSameDocumentAs(version) && _version.CompareAge(version) == 0)
            return;
        _version = version;

        var kinds = Scan(_document);
        var changed = !kinds.AsSpan().SequenceEqual(_kinds);
        _kinds = kinds;
        if (changed) StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    private static LineKind[] Scan(TextDocument doc)
    {
        var kinds = new LineKind[doc.LineCount];
        char fenceChar = '\0';
        var fenceLength = 0;
        var lineIndex = 0;

        // YAML front matter must start on the very first line.
        if (doc.LineCount > 1 && doc.GetText(doc.GetLineByNumber(1)).TrimEnd() == "---")
        {
            for (var n = 2; n <= doc.LineCount; n++)
            {
                var t = doc.GetText(doc.GetLineByNumber(n)).TrimEnd();
                if (t is "---" or "...")
                {
                    for (var k = 0; k < n; k++) kinds[k] = LineKind.FrontMatter;
                    lineIndex = n;
                    break;
                }
            }
        }

        for (; lineIndex < doc.LineCount; lineIndex++)
        {
            var text = doc.GetText(doc.GetLineByNumber(lineIndex + 1));
            var trimmed = text.TrimStart(' ');
            var indent = text.Length - trimmed.Length;

            if (fenceChar == '\0')
            {
                if (indent <= 3 && TryReadFence(trimmed, out var ch, out var len))
                {
                    fenceChar = ch;
                    fenceLength = len;
                    kinds[lineIndex] = LineKind.FenceDelimiter;
                }
            }
            else
            {
                if (indent <= 3 && TryReadFence(trimmed, out var ch, out var len) && ch == fenceChar && len >= fenceLength &&
                    trimmed[len..].Trim().Length == 0)
                {
                    fenceChar = '\0';
                    kinds[lineIndex] = LineKind.FenceDelimiter;
                }
                else
                {
                    kinds[lineIndex] = LineKind.Code;
                }
            }
        }
        return kinds;
    }

    private static bool TryReadFence(string trimmed, out char ch, out int length)
    {
        ch = '\0';
        length = 0;
        if (trimmed.Length < 3 || (trimmed[0] != '`' && trimmed[0] != '~')) return false;
        ch = trimmed[0];
        while (length < trimmed.Length && trimmed[length] == ch) length++;
        // A backtick fence's info string may not contain backticks.
        return length >= 3 && !(ch == '`' && trimmed.IndexOf('`', length) >= 0);
    }
}
