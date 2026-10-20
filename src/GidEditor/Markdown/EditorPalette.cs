using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace GidEditor.Markdown;

/// <summary>Brushes used by the editor colorizer and preview, resolved from the app's theme resources.</summary>
public sealed class EditorPalette
{
    public required IBrush Text { get; init; }
    public required IBrush Muted { get; init; }
    public required IBrush Accent { get; init; }
    public required IBrush Heading { get; init; }
    public required IBrush Link { get; init; }
    public required IBrush CodeText { get; init; }
    public required IBrush CodeBlockText { get; init; }
    public required IBrush CodeBackground { get; init; }
    public required IBrush InlineCodeBackground { get; init; }
    public required IBrush QuoteBar { get; init; }
    public required IBrush Quote { get; init; }
    public required IBrush Border { get; init; }
    public required IBrush Highlight { get; init; }
    public required IBrush TableHeader { get; init; }

    public static EditorPalette Current()
    {
        var app = Application.Current!;
        var theme = app.ActualThemeVariant;
        return new EditorPalette
        {
            Text = Get(app, theme, "Gid.Text"),
            Muted = Get(app, theme, "Gid.TextMuted"),
            Accent = Get(app, theme, "Gid.Accent"),
            Heading = Get(app, theme, "Gid.Heading"),
            Link = Get(app, theme, "Gid.Link"),
            CodeText = Get(app, theme, "Gid.CodeText"),
            CodeBlockText = Get(app, theme, "Gid.CodeBlockText"),
            CodeBackground = Get(app, theme, "Gid.CodeBackground"),
            InlineCodeBackground = Get(app, theme, "Gid.InlineCodeBackground"),
            QuoteBar = Get(app, theme, "Gid.QuoteBar"),
            Quote = Get(app, theme, "Gid.Quote"),
            Border = Get(app, theme, "Gid.Border"),
            Highlight = Get(app, theme, "Gid.Highlight"),
            TableHeader = Get(app, theme, "Gid.TableHeader"),
        };
    }

    private static IBrush Get(Application app, ThemeVariant theme, string key) =>
        app.TryGetResource(key, theme, out var value) && value is IBrush brush ? brush : Brushes.Magenta;
}

public static class EditorFonts
{
    public static readonly FontFamily Sans = new("fonts:Inter#Inter, $Default");

    public static readonly FontFamily Mono = new(
        "Cascadia Code, Cascadia Mono, JetBrains Mono, SF Mono, Menlo, Consolas, Ubuntu Mono, DejaVu Sans Mono, Liberation Mono, monospace");
}
