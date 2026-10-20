using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GidEditor.Controls;

/// <summary>
/// A selectable text block whose link ranges are clickable. Avalonia has no Hyperlink inline, so links are
/// styled spans whose character ranges are recorded and hit-tested on pointer events.
/// </summary>
public sealed class MarkdownTextBlock : SelectableTextBlock
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private string? _pressedLink;

    public List<(int Start, int End, string Url)> Links { get; } = [];

    public event Action<string>? LinkClicked;

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public MarkdownTextBlock()
    {
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnAnyPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Links.Count == 0) return;
        Cursor = LinkAt(e.GetPosition(this)) != null ? HandCursor : Cursor.Default;
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedLink = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? LinkAt(e.GetPosition(this)) : null;
    }

    private void OnAnyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedLink is null) return;
        var link = LinkAt(e.GetPosition(this));
        var pressed = _pressedLink;
        _pressedLink = null;
        // Only treat it as a click if the user did not drag to select text.
        if (link == pressed && SelectionStart == SelectionEnd)
        {
            e.Handled = true;
            LinkClicked?.Invoke(link);
        }
    }

    private string? LinkAt(Point point)
    {
        if (Links.Count == 0) return null;
        var p = new Point(point.X - Padding.Left, point.Y - Padding.Top);
        var hit = TextLayout.HitTestPoint(p);
        if (!hit.IsInside) return null;
        var pos = hit.TextPosition;
        foreach (var (start, end, url) in Links)
            if (pos >= start && pos < end) return url;
        return null;
    }
}
