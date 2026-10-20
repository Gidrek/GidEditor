using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using GidEditor.Markdown;

namespace GidEditor.Controls;

/// <summary>Scrollable, natively rendered markdown preview.</summary>
public sealed class MarkdownViewer : UserControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownViewer, string?>(nameof(Markdown));

    public static readonly StyledProperty<string?> BaseDirectoryProperty =
        AvaloniaProperty.Register<MarkdownViewer, string?>(nameof(BaseDirectory));

    public static readonly StyledProperty<double> BaseFontSizeProperty =
        AvaloniaProperty.Register<MarkdownViewer, double>(nameof(BaseFontSize), 15);

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _content;
    private List<RenderedBlock> _blocks = [];
    private Dictionary<string, Control> _anchors = [];
    private bool _renderPending;

    public MarkdownViewer()
    {
        _content = new StackPanel
        {
            MaxWidth = 820,
            Margin = new Thickness(40, 32, 40, 64),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _content,
        };
        Content = _scroll;
        ActualThemeVariantChanged += (_, _) => ScheduleRender();
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string? BaseDirectory
    {
        get => GetValue(BaseDirectoryProperty);
        set => SetValue(BaseDirectoryProperty, value);
    }

    public double BaseFontSize
    {
        get => GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    /// <summary>Raised for links the viewer does not handle itself (anything except "#anchor" links).</summary>
    public event Action<string>? LinkClicked;

    /// <summary>Raised with the zero-based source line of a task list checkbox that was clicked.</summary>
    public event Action<int>? TaskToggled;

    public ScrollViewer ScrollViewer => _scroll;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == BaseDirectoryProperty ||
            change.Property == BaseFontSizeProperty || change.Property == IsVisibleProperty)
            ScheduleRender();
    }

    /// <summary>Re-renders on the next dispatcher pass; skipped entirely while the viewer is hidden.</summary>
    public void ScheduleRender()
    {
        if (_renderPending) return;
        _renderPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _renderPending = false;
            if (IsEffectivelyVisible) Render();
        }, DispatcherPriority.Background);
    }

    private string? _renderedMarkdown;
    private object? _renderedTheme;

    private void Render()
    {
        var markdown = Markdown ?? "";
        if (markdown == _renderedMarkdown && ReferenceEquals(_renderedTheme, ActualThemeVariant) && _content.Children.Count > 0)
            return;
        _renderedMarkdown = markdown;
        _renderedTheme = ActualThemeVariant;

        var offset = _scroll.Offset;
        var renderer = new MarkdownRenderer(EditorPalette.Current(), BaseDirectory, BaseFontSize);
        renderer.LinkClicked += OnLinkClicked;
        renderer.TaskToggled += line => TaskToggled?.Invoke(line);

        _content.Children.Clear();
        _blocks = renderer.Render(markdown, _content);
        _anchors = renderer.Anchors;

        // Keep the reading position while typing.
        Dispatcher.UIThread.Post(() => _scroll.Offset = offset, DispatcherPriority.Loaded);
    }

    private void OnLinkClicked(string url)
    {
        if (url.StartsWith('#'))
        {
            if (_anchors.TryGetValue(url[1..], out var target))
                ScrollToControl(target);
            return;
        }
        LinkClicked?.Invoke(url);
    }

    private void ScrollToControl(Control control)
    {
        var p = control.TranslatePoint(new Point(0, 0), _content);
        if (p is { } point)
            _scroll.Offset = new Vector(0, Math.Max(0, point.Y + _content.Margin.Top - 12));
    }

    /// <summary>Scrolls so that the content for (fractional, zero-based) source line <paramref name="line"/> is at the top.</summary>
    public void ScrollToSourceLine(double line, bool atEnd)
    {
        if (_blocks.Count == 0) return;
        if (atEnd)
        {
            _scroll.Offset = new Vector(0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height));
            return;
        }

        var i = 0;
        while (i + 1 < _blocks.Count && _blocks[i + 1].Line <= line) i++;

        var top = TopOf(_blocks[i].Control);
        double y;
        if (line < _blocks[i].Line)
        {
            y = 0;
        }
        else if (i + 1 < _blocks.Count)
        {
            var nextTop = TopOf(_blocks[i + 1].Control);
            var span = _blocks[i + 1].Line - _blocks[i].Line;
            var t = span > 0 ? (line - _blocks[i].Line) / span : 0;
            y = top + Math.Clamp(t, 0, 1) * (nextTop - top);
        }
        else
        {
            y = top;
        }

        _scroll.Offset = new Vector(0, Math.Clamp(y, 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height)));
    }

    private double TopOf(Control control) =>
        (control.TranslatePoint(new Point(0, 0), _content)?.Y ?? 0) + _content.Margin.Top - control.Margin.Top;
}
