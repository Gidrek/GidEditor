using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace GidEditor.Markdown;

/// <summary>
/// "Styled source" rendering for the editor: the markdown stays editable as text, but headings are large,
/// emphasis is bold/italic, code is monospaced, links are colored and syntax markers are dimmed.
/// </summary>
public sealed partial class MarkdownColorizer : DocumentColorizingTransformer
{
    private static readonly double[] HeadingScale = [1.75, 1.45, 1.25, 1.12, 1.0, 1.0];

    public MarkdownBlockScanner? Scanner { get; set; }
    public EditorPalette Palette { get; set; } = null!;

    /// <summary>When the whole editor already uses a monospace font, code needs no size compensation.</summary>
    public bool BaseIsMonospace { get; set; }

    [GeneratedRegex(@"^( {0,3})(#{1,6})(?:[ \t]+|$)")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(\s*)((?:>\s?)+)")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"^(\s*)([-*+]|\d{1,9}[.)])([ \t]+)(\[[ xX]\](?:[ \t]|$))?")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^ {0,3}((?:\*[ \t]*){3,}|(?:-[ \t]*){3,}|(?:_[ \t]*){3,})$")]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"^\s*\|.*\|\s*$")]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"(`+)(.+?)\1")]
    private static partial Regex CodeSpanRegex();

    [GeneratedRegex(@"(!?)\[([^\]\n]*)\]\(([^)\s]*)(?:\s+""[^""]*"")?\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<https?://[^>\s]+>|https?://[^\s<>()\[\]]+[^\s<>()\[\].,;:!?'""]")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<![*_\w\\])([*_])(?=[^\s*_])(.+?)(?<=[^\s*_\\])\1(?![*_\w])")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"~~(?=\S)(.+?)(?<=\S)~~")]
    private static partial Regex StrikeRegex();

    [GeneratedRegex(@"==(?=\S)(.+?)(?<=\S)==")]
    private static partial Regex HighlightRegex();

    [GeneratedRegex(@"</?[a-zA-Z][^>\n]*>|<!--.*?-->")]
    private static partial Regex HtmlRegex();

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0) return;
        var text = CurrentContext.Document.GetText(line);
        var kind = Scanner?.GetKind(line.LineNumber) ?? LineKind.Normal;

        switch (kind)
        {
            case LineKind.FenceDelimiter:
                Apply(line.Offset, line.EndOffset, Mono(Palette.Muted));
                return;
            case LineKind.Code:
                Apply(line.Offset, line.EndOffset, Mono(Palette.CodeBlockText));
                return;
            case LineKind.FrontMatter:
                Apply(line.Offset, line.EndOffset, Mono(Palette.Muted));
                return;
        }

        var start = line.Offset;

        if (RuleRegex().IsMatch(text))
        {
            Apply(start, line.EndOffset, Fg(Palette.Muted));
            return;
        }

        var contentStart = 0;

        var heading = HeadingRegex().Match(text);
        if (heading.Success)
        {
            var level = heading.Groups[2].Length;
            var scale = HeadingScale[level - 1];
            Apply(start, line.EndOffset, e =>
            {
                e.TextRunProperties.SetFontRenderingEmSize(e.TextRunProperties.FontRenderingEmSize * scale);
                SetWeight(e, FontWeight.Bold);
                e.TextRunProperties.SetForegroundBrush(Palette.Heading);
            });
            Apply(start, start + heading.Length, Fg(Palette.Muted));
            contentStart = heading.Length;
        }
        else
        {
            var quote = QuoteRegex().Match(text);
            if (quote.Success)
            {
                Apply(start + quote.Length, line.EndOffset, e =>
                {
                    e.TextRunProperties.SetForegroundBrush(Palette.Quote);
                    SetStyle(e, FontStyle.Italic);
                });
                Apply(start + quote.Groups[1].Length, start + quote.Length, Fg(Palette.QuoteBar));
                contentStart = quote.Length;
            }

            var list = ListRegex().Match(text, contentStart);
            if (list.Success && list.Index == contentStart)
            {
                var markerStart = start + list.Index + list.Groups[1].Length;
                Apply(markerStart, markerStart + list.Groups[2].Length, e =>
                {
                    e.TextRunProperties.SetForegroundBrush(Palette.Accent);
                    SetWeight(e, FontWeight.Bold);
                });
                var task = list.Groups[4];
                if (task.Success)
                {
                    var done = task.Value.Contains('x', StringComparison.OrdinalIgnoreCase);
                    Apply(start + task.Index, start + task.Index + 3, Mono(Palette.Accent));
                    if (done)
                        Apply(start + task.Index + task.Length, line.EndOffset, e =>
                        {
                            e.TextRunProperties.SetForegroundBrush(Palette.Muted);
                            e.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
                        });
                }
                contentStart = list.Index + list.Length;
            }
            else if (TableRowRegex().IsMatch(text))
            {
                for (var i = 0; i < text.Length; i++)
                    if (text[i] == '|') Apply(start + i, start + i + 1, Fg(Palette.Muted));
                if (IsTableSeparator(text))
                {
                    Apply(start, line.EndOffset, Fg(Palette.Muted));
                    return;
                }
            }
        }

        ColorizeInlines(text, start, contentStart);
    }

    private void ColorizeInlines(string text, int lineOffset, int from)
    {
        // Ranges already claimed by code spans or link URLs are not styled again.
        var claimed = new List<(int Start, int End)>();
        bool Free(int s, int e)
        {
            foreach (var (cs, ce) in claimed)
                if (s < ce && e > cs) return false;
            return true;
        }

        foreach (Match m in CodeSpanRegex().Matches(text, from))
        {
            var ticks = m.Groups[1].Length;
            Apply(lineOffset + m.Index, lineOffset + m.Index + m.Length, e =>
            {
                SetMono(e);
                e.TextRunProperties.SetForegroundBrush(Palette.CodeText);
                e.TextRunProperties.SetBackgroundBrush(Palette.InlineCodeBackground);
            });
            Apply(lineOffset + m.Index, lineOffset + m.Index + ticks, Fg(Palette.Muted));
            Apply(lineOffset + m.Index + m.Length - ticks, lineOffset + m.Index + m.Length, Fg(Palette.Muted));
            claimed.Add((m.Index, m.Index + m.Length));
        }

        foreach (Match m in LinkRegex().Matches(text, from))
        {
            if (!Free(m.Index, m.Index + m.Length)) continue;
            var isImage = m.Groups[1].Length > 0;
            var label = m.Groups[2];
            var url = m.Groups[3];
            // Markers and URL are dimmed; the label stays readable as a link.
            Apply(lineOffset + m.Index, lineOffset + label.Index, Fg(Palette.Muted));
            Apply(lineOffset + label.Index, lineOffset + label.Index + label.Length, e =>
            {
                e.TextRunProperties.SetForegroundBrush(Palette.Link);
                if (!isImage) e.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
            });
            Apply(lineOffset + label.Index + label.Length, lineOffset + m.Index + m.Length, Fg(Palette.Muted));
            claimed.Add((url.Index, url.Index + url.Length));
        }

        foreach (Match m in UrlRegex().Matches(text, from))
        {
            if (!Free(m.Index, m.Index + m.Length)) continue;
            Apply(lineOffset + m.Index, lineOffset + m.Index + m.Length, e =>
            {
                e.TextRunProperties.SetForegroundBrush(Palette.Link);
                e.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
            });
            claimed.Add((m.Index, m.Index + m.Length));
        }

        foreach (Match m in HtmlRegex().Matches(text, from))
            if (Free(m.Index, m.Index + m.Length))
                Apply(lineOffset + m.Index, lineOffset + m.Index + m.Length, Fg(Palette.Muted));

        StyleDelimited(BoldRegex(), text, lineOffset, from, 2, Free, e => SetWeight(e, FontWeight.Bold));
        StyleDelimited(ItalicRegex(), text, lineOffset, from, 1, Free, e => SetStyle(e, FontStyle.Italic));
        StyleDelimited(StrikeRegex(), text, lineOffset, from, 2, Free,
            e => e.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough));
        StyleDelimited(HighlightRegex(), text, lineOffset, from, 2, Free,
            e => e.TextRunProperties.SetBackgroundBrush(Palette.Highlight));
    }

    private void StyleDelimited(Regex regex, string text, int lineOffset, int from, int markerLength,
        Func<int, int, bool> free, Action<VisualLineElement> style)
    {
        foreach (Match m in regex.Matches(text, from))
        {
            if (!free(m.Index, m.Index + markerLength) || !free(m.Index + m.Length - markerLength, m.Index + m.Length))
                continue;
            Apply(lineOffset + m.Index, lineOffset + m.Index + m.Length, style);
            Apply(lineOffset + m.Index, lineOffset + m.Index + markerLength, Fg(Palette.Muted));
            Apply(lineOffset + m.Index + m.Length - markerLength, lineOffset + m.Index + m.Length, Fg(Palette.Muted));
        }
    }

    private static bool IsTableSeparator(string text)
    {
        var t = text.Trim();
        return t.Length > 2 && t.Contains('-') && t.Trim('|', '-', ':', ' ', '\t').Length == 0;
    }

    private void Apply(int start, int end, Action<VisualLineElement> action)
    {
        if (end > start) ChangeLinePart(start, end, action);
    }

    private static Action<VisualLineElement> Fg(IBrush brush) => e => e.TextRunProperties.SetForegroundBrush(brush);

    private Action<VisualLineElement> Mono(IBrush brush) => e =>
    {
        SetMono(e);
        e.TextRunProperties.SetForegroundBrush(brush);
    };

    private void SetMono(VisualLineElement e)
    {
        var tf = e.TextRunProperties.Typeface;
        e.TextRunProperties.SetTypeface(new Typeface(EditorFonts.Mono, tf.Style, tf.Weight));
        // Monospace looks larger than Inter at the same em size; shrink it slightly to keep lines even.
        if (!BaseIsMonospace)
            e.TextRunProperties.SetFontRenderingEmSize(e.TextRunProperties.FontRenderingEmSize * 0.93);
    }

    private static void SetWeight(VisualLineElement e, FontWeight weight)
    {
        var tf = e.TextRunProperties.Typeface;
        e.TextRunProperties.SetTypeface(new Typeface(tf.FontFamily, tf.Style, weight));
    }

    private static void SetStyle(VisualLineElement e, FontStyle style)
    {
        var tf = e.TextRunProperties.Typeface;
        e.TextRunProperties.SetTypeface(new Typeface(tf.FontFamily, style, tf.Weight));
    }
}
