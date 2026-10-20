using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace GidEditor.Markdown;

public enum LinePrefixKind
{
    Bullet,
    Numbered,
    Task,
    Quote,
}

/// <summary>Formatting commands for the toolbar and keyboard shortcuts. Every command is a single undo step.</summary>
public static partial class MarkdownFormatter
{
    [GeneratedRegex(@"^(\s*)(#{1,6}[ \t]+|#{1,6}$)")]
    private static partial Regex HeadingPrefix();

    [GeneratedRegex(@"^(\s*)(?:[-*+][ \t]+\[[ xX]\][ \t]+|[-*+][ \t]+|\d{1,9}[.)][ \t]+|>[ \t]?)")]
    private static partial Regex AnyLinePrefix();

    [GeneratedRegex(@"^(\s*)([-*+]|(\d{1,9})([.)]))([ \t]+)(\[[ xX]\][ \t]+)?(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^(\s*)((?:>[ \t]?)+)(.*)$")]
    private static partial Regex QuoteLine();

    [GeneratedRegex(@"^(https?://|mailto:|www\.)\S+$")]
    private static partial Regex LooksLikeUrl();

    /// <summary>Wraps the selection (or the word at the caret) in <paramref name="marker"/>, or removes it if already wrapped.</summary>
    public static void ToggleInline(TextEditor editor, string marker)
    {
        var doc = editor.Document;
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;

        if (length == 0)
        {
            (start, length) = WordAt(doc, editor.CaretOffset);
            if (length == 0)
            {
                // Nothing to wrap: insert an empty pair and put the caret inside.
                var caret = editor.CaretOffset;
                doc.Insert(caret, marker + marker);
                editor.Select(caret + marker.Length, 0);
                editor.CaretOffset = caret + marker.Length;
                return;
            }
        }

        var m = marker.Length;
        using (doc.RunUpdate())
        {
            var selected = doc.GetText(start, length);
            if (selected.Length >= 2 * m && selected.StartsWith(marker) && selected.EndsWith(marker))
            {
                doc.Replace(start, length, selected[m..^m]);
                editor.Select(start, length - 2 * m);
            }
            else if (start >= m && start + length + m <= doc.TextLength &&
                     doc.GetText(start - m, m) == marker && doc.GetText(start + length, m) == marker)
            {
                doc.Remove(start + length, m);
                doc.Remove(start - m, m);
                editor.Select(start - m, length);
            }
            else
            {
                doc.Insert(start + length, marker);
                doc.Insert(start, marker);
                editor.Select(start + m, length);
            }
        }
    }

    /// <summary>Sets the heading level of every selected line; applying the same level again removes it.</summary>
    public static void ToggleHeading(TextEditor editor, int level)
    {
        var doc = editor.Document;
        var lines = SelectedLines(editor).ToList();
        var prefix = new string('#', level) + " ";
        var allAtLevel = lines.All(l => HeadingLevel(doc.GetText(l)) == level);

        using (doc.RunUpdate())
        {
            foreach (var line in lines)
            {
                var text = doc.GetText(line);
                var match = HeadingPrefix().Match(text);
                var content = match.Success ? text[match.Length..] : text.TrimStart();
                var indentLength = text.Length - text.TrimStart().Length;
                if (match.Success) indentLength = match.Groups[1].Length;
                // Strip any list/quote marker too; a line is either a heading or a list item.
                var other = AnyLinePrefix().Match(content);
                if (other.Success) content = content[other.Length..];
                doc.Replace(line.Offset + indentLength, line.Length - indentLength, allAtLevel ? content : prefix + content);
            }
        }
        editor.CaretOffset = lines[^1].EndOffset;
    }

    public static int HeadingLevel(string lineText)
    {
        var m = HeadingPrefix().Match(lineText);
        return m.Success ? m.Groups[2].Value.TrimEnd().Length : 0;
    }

    /// <summary>Toggles a list or quote marker on every selected line.</summary>
    public static void ToggleLinePrefix(TextEditor editor, LinePrefixKind kind)
    {
        var doc = editor.Document;
        var lines = SelectedLines(editor).ToList();
        var nonEmpty = lines.Where(l => doc.GetText(l).Trim().Length > 0).ToList();
        if (nonEmpty.Count == 0) nonEmpty = lines;

        var allHave = nonEmpty.All(l => HasPrefix(doc.GetText(l), kind));

        using (doc.RunUpdate())
        {
            var number = 1;
            foreach (var line in nonEmpty)
            {
                var text = doc.GetText(line);
                var indent = text.Length - text.TrimStart().Length;
                var body = text[indent..];
                var existing = AnyLinePrefix().Match(body);
                if (existing.Success) body = body[existing.Length..];
                var heading = HeadingPrefix().Match(body);
                if (heading.Success) body = body[heading.Length..];

                var prefix = allHave ? "" : kind switch
                {
                    LinePrefixKind.Bullet => "- ",
                    LinePrefixKind.Numbered => $"{number++}. ",
                    LinePrefixKind.Task => "- [ ] ",
                    _ => "> ",
                };
                doc.Replace(line.Offset + indent, line.Length - indent, prefix + body);
            }
        }
        editor.CaretOffset = nonEmpty[^1].EndOffset;
    }

    private static bool HasPrefix(string text, LinePrefixKind kind)
    {
        var t = text.TrimStart();
        return kind switch
        {
            LinePrefixKind.Task => Regex.IsMatch(t, @"^[-*+][ \t]+\[[ xX]\]"),
            LinePrefixKind.Bullet => Regex.IsMatch(t, @"^[-*+][ \t]+(?!\[[ xX]\])"),
            LinePrefixKind.Numbered => Regex.IsMatch(t, @"^\d{1,9}[.)][ \t]+"),
            _ => t.StartsWith('>'),
        };
    }

    public static void InsertLink(TextEditor editor)
    {
        var doc = editor.Document;
        var start = editor.SelectionStart;
        var selected = editor.SelectedText;

        if (selected.Length > 0 && LooksLikeUrl().IsMatch(selected.Trim()))
        {
            doc.Replace(start, selected.Length, $"[]({selected.Trim()})");
            editor.Select(start + 1, 0);
            editor.CaretOffset = start + 1;
        }
        else if (selected.Length > 0)
        {
            doc.Replace(start, selected.Length, $"[{selected}](url)");
            editor.Select(start + selected.Length + 3, 3);
        }
        else
        {
            doc.Insert(start, "[text](url)");
            editor.Select(start + 1, 4);
        }
    }

    public static void InsertImageLink(TextEditor editor, string alt, string url)
    {
        var doc = editor.Document;
        var offset = editor.SelectionStart;
        var text = $"![{alt}]({url})";
        doc.Replace(offset, editor.SelectionLength, text);
        editor.Select(offset + text.Length, 0);
        editor.CaretOffset = offset + text.Length;
    }

    public static void InsertCodeBlock(TextEditor editor)
    {
        var doc = editor.Document;
        var selected = editor.SelectedText;
        var start = editor.SelectionStart;
        var lead = NeedsLeadingNewline(doc, start) ? "\n" : "";

        if (selected.Length > 0)
        {
            var block = $"{lead}```\n{selected.TrimEnd('\n')}\n```\n";
            doc.Replace(start, selected.Length, block);
            editor.Select(start + lead.Length + 3, 0);
            editor.CaretOffset = start + lead.Length + 3;
        }
        else
        {
            doc.Insert(start, $"{lead}```\n\n```\n");
            editor.Select(start + lead.Length + 4, 0);
            editor.CaretOffset = start + lead.Length + 4;
        }
    }

    public static void InsertHorizontalRule(TextEditor editor) =>
        InsertBlock(editor, "---\n");

    public static void InsertTable(TextEditor editor)
    {
        const string table =
            "| Column 1 | Column 2 | Column 3 |\n" +
            "| -------- | -------- | -------- |\n" +
            "|          |          |          |\n";
        var offset = InsertBlock(editor, table);
        editor.Select(offset + 2, 8);
    }

    /// <summary>Inserts text on its own line(s) at the caret and returns the offset where it starts.</summary>
    private static int InsertBlock(TextEditor editor, string block)
    {
        var doc = editor.Document;
        var line = doc.GetLineByOffset(editor.CaretOffset);
        var lineText = doc.GetText(line);
        int offset;
        if (lineText.Trim().Length == 0)
        {
            offset = line.Offset;
            doc.Replace(line.Offset, line.Length, block.TrimEnd('\n'));
        }
        else
        {
            offset = line.EndOffset + 2;
            doc.Insert(line.EndOffset, "\n\n" + block.TrimEnd('\n'));
        }
        editor.CaretOffset = offset + block.TrimEnd('\n').Length;
        return offset;
    }

    /// <summary>
    /// Smart Enter: continues list items and quotes on the next line, or ends the list when the item is empty.
    /// Returns false when the default Enter behavior should run.
    /// </summary>
    public static bool HandleEnter(TextEditor editor)
    {
        if (editor.SelectionLength > 0) return false;
        var doc = editor.Document;
        var caret = editor.CaretOffset;
        var line = doc.GetLineByOffset(caret);
        var text = doc.GetText(line);
        if (caret - line.Offset < IndentLength(text)) return false;

        var list = ListItem().Match(text);
        if (list.Success)
        {
            var indent = list.Groups[1].Value;
            var hasTask = list.Groups[6].Success;
            var content = list.Groups[7].Value;
            var markerEnd = text.Length - content.Length;
            if (caret - line.Offset < markerEnd) return false;

            if (content.Trim().Length == 0)
            {
                // Enter on an empty item ends the list.
                doc.Replace(line.Offset, line.Length, "");
                editor.CaretOffset = line.Offset;
                return true;
            }

            string marker;
            if (list.Groups[3].Success)
            {
                var next = long.Parse(list.Groups[3].Value) + 1;
                marker = next + list.Groups[4].Value;
            }
            else
            {
                marker = list.Groups[2].Value;
            }

            var insert = "\n" + indent + marker + list.Groups[5].Value + (hasTask ? "[ ] " : "");
            doc.Insert(caret, insert);
            editor.CaretOffset = caret + insert.Length;
            return true;
        }

        var quote = QuoteLine().Match(text);
        if (quote.Success && caret - line.Offset >= quote.Groups[1].Length + quote.Groups[2].Length)
        {
            if (quote.Groups[3].Value.Trim().Length == 0)
            {
                doc.Replace(line.Offset, line.Length, "");
                editor.CaretOffset = line.Offset;
                return true;
            }
            var insert = "\n" + quote.Groups[1].Value + quote.Groups[2].Value;
            doc.Insert(caret, insert);
            editor.CaretOffset = caret + insert.Length;
            return true;
        }

        return false;
    }

    /// <summary>Tab / Shift+Tab on list items indents or outdents the whole item. Returns false if not on a list line.</summary>
    public static bool HandleTab(TextEditor editor, bool outdent)
    {
        var doc = editor.Document;
        var lines = SelectedLines(editor).ToList();
        if (!lines.All(l => ListItem().IsMatch(doc.GetText(l)))) return false;

        using (doc.RunUpdate())
        {
            foreach (var line in lines)
            {
                if (outdent)
                {
                    var text = doc.GetText(line);
                    var remove = Math.Min(IndentLength(text), text.StartsWith('\t') ? 1 : 4);
                    if (remove > 0) doc.Remove(line.Offset, remove);
                }
                else
                {
                    doc.Insert(line.Offset, "    ");
                }
            }
        }
        return true;
    }

    /// <summary>Flips the "[ ]" / "[x]" checkbox that starts at <paramref name="offset"/> (pointing at '[').</summary>
    public static void ToggleTaskAt(TextDocument doc, int offset)
    {
        if (offset < 0 || offset + 3 > doc.TextLength) return;
        var box = doc.GetText(offset, 3);
        if (box is not ("[ ]" or "[x]" or "[X]")) return;
        doc.Replace(offset + 1, 1, box[1] == ' ' ? "x" : " ");
    }

    private static IEnumerable<DocumentLine> SelectedLines(TextEditor editor)
    {
        var doc = editor.Document;
        var first = doc.GetLineByOffset(editor.SelectionStart);
        var endOffset = editor.SelectionStart + editor.SelectionLength;
        var last = doc.GetLineByOffset(endOffset);
        // A selection that ends at the very start of a line does not include that line.
        if (last != first && endOffset == last.Offset) last = last.PreviousLine;
        for (var line = first; line != null; line = line.NextLine)
        {
            yield return line;
            if (line == last) break;
        }
    }

    private static (int Start, int Length) WordAt(TextDocument doc, int offset)
    {
        var start = offset;
        var end = offset;
        while (start > 0 && IsWordChar(doc.GetCharAt(start - 1))) start--;
        while (end < doc.TextLength && IsWordChar(doc.GetCharAt(end))) end++;
        return (start, end - start);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '\'' or '-';

    private static int IndentLength(string text) => text.Length - text.TrimStart(' ', '\t').Length;

    private static bool NeedsLeadingNewline(TextDocument doc, int offset)
    {
        var line = doc.GetLineByOffset(offset);
        return offset > line.Offset;
    }
}
