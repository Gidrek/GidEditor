using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace GidEditor.Views;

/// <summary>Small modal dialog for confirmations and single-line text prompts.</summary>
public sealed class DialogWindow : Window
{
    private readonly TextBox? _input;
    private int _result = -1;

    private DialogWindow(string title, string message, IReadOnlyList<string> buttons, int destructiveIndex, string? initialInput)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Gid.Background"));

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Spacing = 10 };

        var heading = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold };
        heading.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Gid.Heading"));
        panel.Children.Add(heading);

        if (!string.IsNullOrEmpty(message))
        {
            var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 };
            body.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Gid.TextMuted"));
            panel.Children.Add(body);
        }

        if (initialInput != null)
        {
            _input = new TextBox { Text = initialInput, Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(_input);
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
        };
        // Buttons are given primary-first; show them primary-last so the main action sits at the right edge.
        for (var i = buttons.Count - 1; i >= 0; i--)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (i == 0) button.Classes.Add(destructiveIndex == 0 ? "danger" : "primary");
            else if (i == destructiveIndex) button.Classes.Add("danger");
            button.Click += (_, _) => Finish(index);
            row.Children.Add(button);
        }
        panel.Children.Add(row);

        Styles.Add(new Style(x => x.OfType<Button>().Class("danger"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.Parse("#D64545"))),
                new Setter(Button.ForegroundProperty, Brushes.White),
            },
        });

        Content = panel;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(-1);
            else if (e.Key == Key.Enter) Finish(0);
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_input != null)
            {
                _input.Focus();
                _input.SelectAll();
            }
            else
            {
                row.Children.OfType<Button>().LastOrDefault()?.Focus();
            }
        });
    }

    private void Finish(int index)
    {
        _result = index;
        Close();
    }

    public static async Task<int> ChooseAsync(Window owner, string title, string message, IReadOnlyList<string> buttons, int destructiveIndex)
    {
        var dialog = new DialogWindow(title, message, buttons, destructiveIndex, null);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    public static async Task<string?> PromptAsync(Window owner, string title, string message, string initial, string okText)
    {
        var dialog = new DialogWindow(title, message, [okText, "Cancel"], -1, initial);
        await dialog.ShowDialog(owner);
        return dialog._result == 0 ? dialog._input!.Text?.Trim() : null;
    }
}
