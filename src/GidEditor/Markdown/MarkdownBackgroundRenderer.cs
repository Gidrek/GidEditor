using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace GidEditor.Markdown;

/// <summary>Paints a tinted panel behind fenced code blocks and a bar to the left of block quotes.</summary>
public sealed class MarkdownBackgroundRenderer : IBackgroundRenderer
{
    public MarkdownBlockScanner? Scanner { get; set; }
    public EditorPalette Palette { get; set; } = null!;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Scanner is null || !textView.VisualLinesValid) return;

        var width = textView.Bounds.Width;
        var scrollY = textView.ScrollOffset.Y;
        var document = textView.Document;

        foreach (var visualLine in textView.VisualLines)
        {
            var line = visualLine.FirstDocumentLine;
            var top = visualLine.VisualTop - scrollY;
            var kind = Scanner.GetKind(line.LineNumber);

            if (kind is LineKind.Code or LineKind.FenceDelimiter)
            {
                // Bleed a few pixels to the left so the text does not touch the panel edge.
                drawingContext.FillRectangle(Palette.CodeBackground, new Rect(-8, top, width + 8, visualLine.Height));
                continue;
            }

            var text = document.GetText(line.Offset, System.Math.Min(line.Length, 8)).TrimStart();
            if (text.StartsWith('>'))
                drawingContext.FillRectangle(Palette.QuoteBar, new Rect(-10, top, 3, visualLine.Height));
        }
    }
}
