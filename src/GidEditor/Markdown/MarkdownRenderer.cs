using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GidEditor.Controls;
using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdTable = Markdig.Extensions.Tables.Table;

namespace GidEditor.Markdown;

public sealed class RenderedBlock(int line, Control control)
{
    /// <summary>Zero-based source line where the block starts.</summary>
    public int Line { get; } = line;
    public Control Control { get; } = control;
}

/// <summary>Turns a Markdig syntax tree into native Avalonia controls for the preview pane.</summary>
public sealed class MarkdownRenderer
{
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .UsePreciseSourceLocation()
        .Build();

    private static readonly double[] HeadingScale = [2.0, 1.6, 1.3, 1.12, 1.0, 0.92];

    private readonly EditorPalette _palette;
    private readonly string? _baseDirectory;
    private readonly double _fontSize;

    public MarkdownRenderer(EditorPalette palette, string? baseDirectory, double fontSize)
    {
        _palette = palette;
        _baseDirectory = baseDirectory;
        _fontSize = fontSize;
    }

    public event Action<string>? LinkClicked;

    /// <summary>Raised with the zero-based source line of a task list item whose checkbox was clicked.</summary>
    public event Action<int>? TaskToggled;

    public Dictionary<string, Control> Anchors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<RenderedBlock> Render(string markdown, Panel target)
    {
        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var blocks = new List<RenderedBlock>();
        foreach (var block in document)
        {
            var control = RenderBlock(block, _palette.Text, depth: 0);
            if (control is null) continue;
            target.Children.Add(control);
            blocks.Add(new RenderedBlock(block.Line, control));
        }
        return blocks;
    }

    private Control? RenderBlock(MdBlock block, IBrush foreground, int depth) => block switch
    {
        YamlFrontMatterBlock => null,
        LinkReferenceDefinitionGroup => null,
        BlankLineBlock => null,
        HeadingBlock h => RenderHeading(h),
        ParagraphBlock p => RenderParagraph(p, foreground),
        QuoteBlock q => RenderQuote(q, depth),
        ListBlock l => RenderList(l, foreground, depth),
        MdTable t => RenderTable(t, foreground),
        CodeBlock c => RenderCode(c),
        ThematicBreakBlock => new Border
        {
            Height = 1,
            Background = _palette.Border,
            Margin = new Thickness(0, 12, 0, 20),
        },
        HtmlBlock html => RenderHtmlBlock(html),
        FootnoteGroup f => RenderFootnotes(f, depth),
        ContainerBlock container => RenderContainer(container, foreground, depth),
        LeafBlock leaf when leaf.Inline != null => RenderParagraphLike(leaf.Inline, foreground, new Thickness(0, 0, 0, 12)),
        _ => null,
    };

    private Control RenderContainer(ContainerBlock container, IBrush foreground, int depth)
    {
        var panel = new StackPanel();
        foreach (var child in container)
        {
            var c = RenderBlock(child, foreground, depth);
            if (c != null) panel.Children.Add(c);
        }
        return panel;
    }

    private Control RenderHeading(HeadingBlock heading)
    {
        var level = Math.Clamp(heading.Level, 1, 6);
        var tb = NewTextBlock(_palette.Heading);
        tb.FontSize = _fontSize * HeadingScale[level - 1];
        tb.FontWeight = level <= 2 ? FontWeight.Bold : FontWeight.SemiBold;
        tb.LineHeight = tb.FontSize * 1.3;
        AddInlines(tb, heading.Inline);

        Control result = tb;
        if (level <= 2)
        {
            result = new Border
            {
                BorderBrush = _palette.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 6),
                Child = tb,
            };
        }
        result.Margin = new Thickness(0, level <= 2 ? 22 : 16, 0, level <= 2 ? 14 : 8);

        var id = heading.GetAttributes().Id;
        if (!string.IsNullOrEmpty(id)) Anchors.TryAdd(id, result);
        return result;
    }

    private Control RenderParagraph(ParagraphBlock paragraph, IBrush foreground)
    {
        // A paragraph that is just an image renders as a block image.
        if (paragraph.Inline?.FirstChild is LinkInline { IsImage: true } img && img.NextSibling is null)
        {
            var image = CreateImage(img.Url ?? "", AltText(img), block: true);
            image.Margin = new Thickness(0, 4, 0, 14);
            return image;
        }
        return RenderParagraphLike(paragraph.Inline, foreground, new Thickness(0, 0, 0, 12));
    }

    private MarkdownTextBlock RenderParagraphLike(ContainerInline? inline, IBrush foreground, Thickness margin)
    {
        var tb = NewTextBlock(foreground);
        tb.Margin = margin;
        AddInlines(tb, inline);
        return tb;
    }

    private Control RenderQuote(QuoteBlock quote, int depth)
    {
        var panel = new StackPanel();
        foreach (var child in quote)
        {
            var c = RenderBlock(child, _palette.Quote, depth);
            if (c != null) panel.Children.Add(c);
        }
        TrimLastMargin(panel);
        return new Border
        {
            BorderBrush = _palette.QuoteBar,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(14, 2, 0, 2),
            Margin = new Thickness(0, 0, 0, 14),
            Child = panel,
        };
    }

    private Control RenderList(ListBlock list, IBrush foreground, int depth)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, depth == 0 ? 12 : 0) };
        var number = 1;
        if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start)) number = start;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(0, 0, 0, list.IsLoose ? 8 : 2),
            };

            var task = (item.FirstOrDefault() as ParagraphBlock)?.Inline?.FirstChild as TaskList;
            Control marker;
            if (task != null)
            {
                var check = new CheckBox
                {
                    IsChecked = task.Checked,
                    MinWidth = 0,
                    MinHeight = 0,
                    // One text line tall, so the box centers on the first line of the item.
                    Height = _fontSize * 1.6,
                    Padding = new Thickness(0),
                    // Text sits slightly above the line box's center (x-height); lift the box to match.
                    Margin = new Thickness(0, -2, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                };
                var line = task.Line;
                check.IsCheckedChanged += (_, _) => TaskToggled?.Invoke(line);
                marker = check;
            }
            else
            {
                var text = list.IsOrdered
                    ? number + (list.OrderedDelimiter == ')' ? ")" : ".")
                    : (depth % 3) switch { 0 => "•", 1 => "◦", _ => "▪" };
                marker = new TextBlock
                {
                    Text = text,
                    Foreground = list.IsOrdered ? _palette.Muted : _palette.Accent,
                    FontSize = _fontSize,
                    LineHeight = _fontSize * 1.6,
                    MinWidth = list.IsOrdered ? 24 : 18,
                    FontWeight = list.IsOrdered ? FontWeight.Normal : FontWeight.Bold,
                    TextAlignment = TextAlignment.Left,
                    Margin = new Thickness(2, 0, 4, 0),
                };
            }
            number++;
            Grid.SetColumn(marker, 0);
            grid.Children.Add(marker);

            var content = new StackPanel();
            foreach (var child in item)
            {
                var c = RenderBlock(child, task?.Checked == true ? _palette.Muted : foreground, depth + 1);
                if (c is null) continue;
                if (!list.IsLoose && child is ParagraphBlock) c.Margin = new Thickness(0, 0, 0, 2);
                content.Children.Add(c);
            }
            TrimLastMargin(content);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
            panel.Children.Add(grid);
        }
        return panel;
    }

    private Control RenderTable(MdTable table, IBrush foreground)
    {
        var grid = new Grid();
        var columnCount = Math.Max(table.ColumnDefinitions.Count,
            table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max());
        for (var i = 0; i < columnCount; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var rowIndex = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            foreach (var cell in row.OfType<TableCell>())
            {
                var col = cell.ColumnIndex >= 0 ? cell.ColumnIndex : row.IndexOf(cell);
                var align = col < table.ColumnDefinitions.Count ? table.ColumnDefinitions[col].Alignment : null;

                var tb = NewTextBlock(foreground);
                tb.TextWrapping = TextWrapping.Wrap;
                tb.MaxWidth = 420;
                tb.TextAlignment = align switch
                {
                    TableColumnAlign.Center => TextAlignment.Center,
                    TableColumnAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };
                if (row.IsHeader) tb.FontWeight = FontWeight.SemiBold;
                foreach (var para in cell.OfType<LeafBlock>()) AddInlines(tb, para.Inline);

                var border = new Border
                {
                    Child = tb,
                    Padding = new Thickness(12, 7),
                    BorderBrush = _palette.Border,
                    BorderThickness = new Thickness(col == 0 ? 0 : 1, rowIndex == 0 ? 0 : 1, 0, 0),
                    Background = row.IsHeader ? _palette.TableHeader : null,
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, Math.Min(col, columnCount - 1));
                Grid.SetColumnSpan(border, Math.Max(1, cell.ColumnSpan));
                grid.Children.Add(border);
            }
            rowIndex++;
        }

        var frame = new Border
        {
            BorderBrush = _palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = grid,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = frame,
            Margin = new Thickness(0, 0, 0, 14),
        };
    }

    private Control RenderCode(CodeBlock code)
    {
        var text = code.Lines.ToString().TrimEnd('\n', '\r');
        var language = (code as FencedCodeBlock)?.Info?.Trim() ?? (code is MathBlock ? "math" : "");

        var tb = new SelectableTextBlock
        {
            Text = text,
            FontFamily = EditorFonts.Mono,
            FontSize = _fontSize * 0.88,
            LineHeight = _fontSize * 0.88 * 1.5,
            Foreground = _palette.CodeBlockText,
            TextWrapping = TextWrapping.NoWrap,
        };
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = tb,
            Padding = new Thickness(16, 14, 16, 14),
        };

        var grid = new Grid();
        grid.Children.Add(scroller);

        var copy = new Button
        {
            Content = "Copy",
            FontSize = 11,
            Padding = new Thickness(8, 2),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0,
            Classes = { "code-copy" },
        };
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(copy)?.Clipboard;
            if (clipboard is null) return;
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
            copy.Content = "Copied";
            DispatcherTimer.RunOnce(() => copy.Content = "Copy", TimeSpan.FromSeconds(1.5));
        };
        grid.Children.Add(copy);

        if (!string.IsNullOrEmpty(language))
        {
            grid.Children.Add(new TextBlock
            {
                Text = language,
                FontSize = 11,
                Foreground = _palette.Muted,
                Margin = new Thickness(0, 6, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Classes = { "code-lang" },
            });
        }

        var border = new Border
        {
            Background = _palette.CodeBackground,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 2, 0, 14),
            Child = grid,
            Classes = { "code-block" },
        };
        return border;
    }

    private Control RenderHtmlBlock(HtmlBlock html)
    {
        var text = html.Lines.ToString().Trim();
        // Comments are invisible, as in a browser.
        if (html.Type == HtmlBlockType.Comment || text.StartsWith("<!--")) return new Panel();
        return new SelectableTextBlock
        {
            Text = text,
            FontFamily = EditorFonts.Mono,
            FontSize = _fontSize * 0.85,
            Foreground = _palette.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
    }

    private Control RenderFootnotes(FootnoteGroup group, int depth)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(new Border { Height = 1, Background = _palette.Border, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var footnote in group.OfType<Footnote>())
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(new TextBlock
            {
                Text = $"{footnote.Order}.",
                Foreground = _palette.Muted,
                FontSize = _fontSize * 0.85,
                Margin = new Thickness(0, 0, 8, 0),
            });
            var content = (StackPanel)RenderContainer(footnote, _palette.Muted, depth);
            foreach (var c in content.Children.OfType<TextBlock>()) c.FontSize = _fontSize * 0.85;
            TrimLastMargin(content);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
            panel.Children.Add(grid);
        }
        return panel;
    }

    // ---------- Inlines ----------

    private MarkdownTextBlock NewTextBlock(IBrush foreground)
    {
        var tb = new MarkdownTextBlock
        {
            Foreground = foreground,
            FontSize = _fontSize,
            LineHeight = _fontSize * 1.6,
            TextWrapping = TextWrapping.Wrap,
            Inlines = new InlineCollection(),
        };
        tb.LinkClicked += url => LinkClicked?.Invoke(url);
        return tb;
    }

    private void AddInlines(MarkdownTextBlock tb, ContainerInline? container)
    {
        if (container is null) return;
        var builder = new InlineBuilder(this, tb);
        foreach (var inline in container)
            builder.Add(inline, tb.Inlines!);
    }

    private Control CreateImage(string url, string alt, bool block)
    {
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (!block) image.MaxHeight = _fontSize * 1.6 * 4;

        var fallback = new TextBlock
        {
            Text = string.IsNullOrEmpty(alt) ? $"🖼 {url}" : $"🖼 {alt}",
            Foreground = _palette.Muted,
            FontStyle = FontStyle.Italic,
        };

        var host = new Panel { HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(host, string.IsNullOrEmpty(alt) ? url : alt);

        void Show(Avalonia.Media.Imaging.Bitmap? bitmap)
        {
            host.Children.Clear();
            if (bitmap is null)
            {
                host.Children.Add(fallback);
                return;
            }
            image.Source = bitmap;
            // Hi-DPI screenshots would otherwise render at twice their intended size; cap at natural size.
            image.MaxWidth = bitmap.Size.Width;
            host.Children.Add(image);
        }

        if (ImageLoader.IsRemote(url))
        {
            host.Children.Add(new TextBlock { Text = "Loading image…", Foreground = _palette.Muted, FontStyle = FontStyle.Italic });
            ImageLoader.LoadRemoteAsync(url).ContinueWith(t =>
                Dispatcher.UIThread.Post(() => Show(t.Result)));
        }
        else
        {
            var path = ImageLoader.ResolveLocalPath(url, _baseDirectory);
            Show(path is null ? null : ImageLoader.LoadLocal(path));
        }
        return host;
    }

    private static string AltText(LinkInline link)
    {
        var parts = new List<string>();
        foreach (var d in link.Descendants<LiteralInline>()) parts.Add(d.Content.ToString());
        return string.Concat(parts);
    }

    private static void TrimLastMargin(StackPanel panel)
    {
        if (panel.Children.Count > 0 && panel.Children[^1] is Control last)
            last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
    }

    /// <summary>Builds Avalonia inlines while tracking character positions so link ranges can be hit-tested.</summary>
    private sealed class InlineBuilder(MarkdownRenderer owner, MarkdownTextBlock target)
    {
        private int _position;

        public void Add(MdInline inline, InlineCollection into)
        {
            switch (inline)
            {
                case TaskList:
                    // Rendered as a checkbox in the list gutter.
                    break;
                case LiteralInline lit:
                    AddRun(into, lit.Content.ToString());
                    break;
                case LineBreakInline br:
                    AddRun(into, br.IsHard ? "\n" : " ");
                    break;
                case CodeInline code:
                    AddRun(into, code.Content, r =>
                    {
                        r.FontFamily = EditorFonts.Mono;
                        r.FontSize = owner._fontSize * 0.9;
                        r.Background = owner._palette.InlineCodeBackground;
                        r.Foreground = owner._palette.CodeText;
                    });
                    break;
                case HtmlEntityInline entity:
                    AddRun(into, entity.Transcoded.ToString());
                    break;
                case HtmlInline html:
                    if (html.Tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase)) AddRun(into, "\n");
                    break;
                case MathInline math:
                    AddRun(into, math.Content.ToString(), r => r.FontFamily = EditorFonts.Mono);
                    break;
                case FootnoteLink fn when !fn.IsBackLink:
                    AddRun(into, $"[{fn.Index}]", r =>
                    {
                        r.FontSize = owner._fontSize * 0.75;
                        r.Foreground = owner._palette.Link;
                        r.BaselineAlignment = BaselineAlignment.Superscript;
                    });
                    break;
                case FootnoteLink:
                    break;
                case AutolinkInline auto:
                    AddLink(into, auto.IsEmail ? "mailto:" + auto.Url : auto.Url, s => AddRun(s.Inlines, auto.Url));
                    break;
                case LinkInline { IsImage: true } img:
                {
                    var control = owner.CreateImage(img.Url ?? "", AltText(img), block: false);
                    into.Add(new InlineUIContainer(control) { BaselineAlignment = BaselineAlignment.Bottom });
                    _position += 1;
                    break;
                }
                case LinkInline link:
                    AddLink(into, link.GetDynamicUrl?.Invoke() ?? link.Url ?? "", s => AddChildren(link, s.Inlines));
                    break;
                case EmphasisInline em:
                    AddEmphasis(em, into);
                    break;
                case ContainerInline container:
                    AddChildren(container, into);
                    break;
            }
        }

        private void AddEmphasis(EmphasisInline em, InlineCollection into)
        {
            Span span = (em.DelimiterChar, em.DelimiterCount) switch
            {
                ('*' or '_', >= 2) => new Bold(),
                ('*' or '_', _) => new Italic(),
                ('~', 2) => new Span { TextDecorations = TextDecorations.Strikethrough },
                ('~', 1) => new Span { BaselineAlignment = BaselineAlignment.Subscript, FontSize = owner._fontSize * 0.75 },
                ('^', _) => new Span { BaselineAlignment = BaselineAlignment.Superscript, FontSize = owner._fontSize * 0.75 },
                ('+', _) => new Underline(),
                ('=', _) => new Span { Background = owner._palette.Highlight },
                _ => new Span(),
            };
            into.Add(span);
            AddChildren(em, span.Inlines);
        }

        private void AddLink(InlineCollection into, string url, Action<Span> fill)
        {
            var span = new Span { Foreground = owner._palette.Link, TextDecorations = TextDecorations.Underline };
            into.Add(span);
            var start = _position;
            fill(span);
            target.Links.Add((start, _position, url));
        }

        private void AddChildren(ContainerInline container, InlineCollection into)
        {
            foreach (var child in container) Add(child, into);
        }

        private void AddRun(InlineCollection into, string text, Action<Run>? style = null)
        {
            if (text.Length == 0) return;
            var run = new Run(text);
            style?.Invoke(run);
            into.Add(run);
            _position += text.Length;
        }
    }
}
