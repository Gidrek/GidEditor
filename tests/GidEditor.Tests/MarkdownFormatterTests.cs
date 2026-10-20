using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using GidEditor.Markdown;
using Xunit;

namespace GidEditor.Tests;

public class MarkdownFormatterTests
{
    private static TextEditor EditorWith(string text, int selStart, int selLength = 0)
    {
        var editor = new TextEditor { Document = new TextDocument(text) };
        editor.Select(selStart, selLength);
        editor.CaretOffset = selStart + selLength;
        return editor;
    }

    [AvaloniaFact]
    public void Bold_wraps_selection_and_toggles_back()
    {
        var editor = EditorWith("hello world", 6, 5);
        MarkdownFormatter.ToggleInline(editor, "**");
        Assert.Equal("hello **world**", editor.Text);
        Assert.Equal("world", editor.SelectedText);

        MarkdownFormatter.ToggleInline(editor, "**");
        Assert.Equal("hello world", editor.Text);
    }

    [AvaloniaFact]
    public void Bold_without_selection_wraps_word_at_caret()
    {
        var editor = EditorWith("hello world", 8);
        MarkdownFormatter.ToggleInline(editor, "**");
        Assert.Equal("hello **world**", editor.Text);
    }

    [AvaloniaFact]
    public void Inline_marker_on_empty_line_inserts_pair_with_caret_inside()
    {
        var editor = EditorWith("", 0);
        MarkdownFormatter.ToggleInline(editor, "*");
        Assert.Equal("**", editor.Text);
        Assert.Equal(1, editor.CaretOffset);
    }

    [AvaloniaFact]
    public void Heading_sets_replaces_and_removes_level()
    {
        var editor = EditorWith("Title", 0);
        MarkdownFormatter.ToggleHeading(editor, 1);
        Assert.Equal("# Title", editor.Text);
        MarkdownFormatter.ToggleHeading(editor, 2);
        Assert.Equal("## Title", editor.Text);
        MarkdownFormatter.ToggleHeading(editor, 2);
        Assert.Equal("Title", editor.Text);
    }

    [AvaloniaFact]
    public void Numbered_list_numbers_every_selected_line_and_toggles_off()
    {
        var editor = EditorWith("one\ntwo\nthree", 0, 13);
        MarkdownFormatter.ToggleLinePrefix(editor, LinePrefixKind.Numbered);
        Assert.Equal("1. one\n2. two\n3. three", editor.Text);

        editor.Select(0, editor.Text.Length);
        MarkdownFormatter.ToggleLinePrefix(editor, LinePrefixKind.Numbered);
        Assert.Equal("one\ntwo\nthree", editor.Text);
    }

    [AvaloniaFact]
    public void Bullet_replaces_existing_task_marker()
    {
        var editor = EditorWith("- [ ] item", 3);
        MarkdownFormatter.ToggleLinePrefix(editor, LinePrefixKind.Bullet);
        Assert.Equal("- item", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_continues_lists_and_increments_numbers()
    {
        var editor = EditorWith("1. first", 8);
        Assert.True(MarkdownFormatter.HandleEnter(editor));
        Assert.Equal("1. first\n2. ", editor.Text);

        editor = EditorWith("- [x] done", 10);
        Assert.True(MarkdownFormatter.HandleEnter(editor));
        Assert.Equal("- [x] done\n- [ ] ", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_on_empty_item_ends_the_list()
    {
        var editor = EditorWith("- a\n- ", 6);
        Assert.True(MarkdownFormatter.HandleEnter(editor));
        Assert.Equal("- a\n", editor.Text);
    }

    [AvaloniaFact]
    public void Enter_on_plain_text_uses_default_behavior()
    {
        var editor = EditorWith("plain", 5);
        Assert.False(MarkdownFormatter.HandleEnter(editor));
    }

    [AvaloniaFact]
    public void Link_wraps_selected_text_and_selects_url_placeholder()
    {
        var editor = EditorWith("see docs", 4, 4);
        MarkdownFormatter.InsertLink(editor);
        Assert.Equal("see [docs](url)", editor.Text);
        Assert.Equal("url", editor.SelectedText);
    }

    [AvaloniaFact]
    public void Tab_indents_list_items_and_shift_tab_outdents()
    {
        var editor = EditorWith("- item", 3);
        Assert.True(MarkdownFormatter.HandleTab(editor, outdent: false));
        Assert.Equal("    - item", editor.Text);
        Assert.True(MarkdownFormatter.HandleTab(editor, outdent: true));
        Assert.Equal("- item", editor.Text);
    }

    [Fact]
    public void Toggle_task_flips_checkbox()
    {
        var doc = new TextDocument("- [ ] task");
        MarkdownFormatter.ToggleTaskAt(doc, 2);
        Assert.Equal("- [x] task", doc.Text);
        MarkdownFormatter.ToggleTaskAt(doc, 2);
        Assert.Equal("- [ ] task", doc.Text);
    }
}
