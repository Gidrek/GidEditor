using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GidEditor.Controls;
using Xunit;

namespace GidEditor.Tests;

public class PreviewInteractionTests
{
    private static (Window Window, MarkdownViewer Viewer) Show(string markdown)
    {
        var viewer = new MarkdownViewer { Markdown = markdown };
        var window = new Window { Width = 900, Height = 600, Content = viewer };
        window.Show();
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        return (window, viewer);
    }

    private static void ClickCharacter(Window window, MarkdownTextBlock block, int charIndex)
    {
        var rect = block.TextLayout.HitTestTextPosition(charIndex);
        var local = new Point(rect.X + rect.Width / 2 + block.Padding.Left, rect.Y + rect.Height / 2 + block.Padding.Top);
        var point = block.TranslatePoint(local, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    [AvaloniaTheory]
    [InlineData("Read the [docs](https://example.com/docs) today.", "Read the ", "https://example.com/docs")]
    [InlineData("**Bold** and `code` then [x](https://b.example)", "Bold and code then ", "https://b.example")]
    [InlineData("Auto <https://c.example> link", "Auto ", "https://c.example")]
    public void Clicking_link_text_raises_link_clicked(string markdown, string textBeforeLink, string expectedUrl)
    {
        var (window, viewer) = Show(markdown);
        string? clicked = null;
        viewer.LinkClicked += url => clicked = url;

        var block = window.GetVisualDescendants().OfType<MarkdownTextBlock>().Single();
        ClickCharacter(window, block, textBeforeLink.Length);
        Assert.Equal(expectedUrl, clicked);

        // Clicking plain text before the link does nothing.
        clicked = null;
        ClickCharacter(window, block, 1);
        Assert.Null(clicked);
    }

    [AvaloniaFact]
    public void Clicking_task_checkbox_reports_its_source_line()
    {
        var (window, viewer) = Show("# List\n\n- [ ] first\n- [x] second\n");
        int? line = null;
        viewer.TaskToggled += l => line = l;

        var boxes = window.GetVisualDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(2, boxes.Count);
        Assert.True(boxes[1].IsChecked);

        boxes[1].IsChecked = false;
        Assert.Equal(3, line);
    }
}
