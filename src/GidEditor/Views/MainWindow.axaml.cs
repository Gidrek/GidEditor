using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using GidEditor.Markdown;
using GidEditor.Models;
using GidEditor.Services;
using GidEditor.ViewModels;

namespace GidEditor.Views;

public partial class MainWindow : Window, IUiServices
{
    private const double EditorMaxTextWidth = 760;

    private readonly MainViewModel _vm;
    private readonly MarkdownColorizer _colorizer = new();
    private readonly MarkdownBackgroundRenderer _backgroundRenderer = new();
    private readonly TextDocument _emptyDocument = new();
    private readonly KeyModifiers _primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    private DocumentViewModel? _shownDocument;
    private MarkdownBlockScanner? _scanner;
    private bool _closeConfirmed;

    public MainWindow() : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        vm.Attach(this);

        RestoreWindowBounds();
        SetupEditor();
        SetupShortcutHints();

        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.NavigateRequested += NavigateTo;
        vm.FocusSearchRequested += () => Dispatcher.UIThread.Post(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }, DispatcherPriority.Loaded);

        Preview.LinkClicked += url => _vm.OpenLink(url);
        Preview.TaskToggled += OnPreviewTaskToggled;
        FileTree.Tapped += OnFileTreeTapped;
        FileTree.KeyDown += OnFileTreeKeyDown;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Application.Current!.ActualThemeVariantChanged += (_, _) => ApplyPalette();

        ApplyPalette();
        ApplyEditorFont();
        ApplySidebarLayout();
        ApplyViewMode();
        ShowDocument(null);

        Opened += (_, _) => _vm.Restore();
    }

    // ================================================================ Editor setup

    private void SetupEditor()
    {
        var options = Editor.Options;
        options.EnableHyperlinks = false;
        options.EnableEmailHyperlinks = false;
        options.ConvertTabsToSpaces = true;
        options.IndentationSize = 4;
        options.HighlightCurrentLine = false;
        options.AllowScrollBelowDocument = true;
        options.CutCopyWholeLine = true;
        options.EnableRectangularSelection = true;

        Editor.Document = _emptyDocument;
        Editor.IsReadOnly = true;

        var textView = Editor.TextArea.TextView;
        textView.LineTransformers.Add(_colorizer);
        textView.BackgroundRenderers.Add(_backgroundRenderer);
        textView.ScrollOffsetChanged += (_, _) => SyncPreviewScroll();

        Editor.TextArea.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretStatus();
        Editor.SizeChanged += (_, _) => UpdateEditorPadding();

        Editor.ContextMenu = BuildEditorContextMenu();
    }

    private ContextMenu BuildEditorContextMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action action, string? gesture = null)
        {
            var item = new MenuItem { Header = header };
            if (gesture != null) item.InputGesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => action();
            return item;
        }
        var primary = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
        menu.Items.Add(Item("Cut", () => Editor.Cut(), $"{primary}+X"));
        menu.Items.Add(Item("Copy", () => Editor.Copy(), $"{primary}+C"));
        menu.Items.Add(Item("Paste", () => _ = PasteAsync(), $"{primary}+V"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Bold", () => Format("bold"), $"{primary}+B"));
        menu.Items.Add(Item("Italic", () => Format("italic"), $"{primary}+I"));
        menu.Items.Add(Item("Link", () => Format("link"), $"{primary}+K"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Select All", () => Editor.SelectAll(), $"{primary}+A"));
        return menu;
    }

    private void ApplyPalette()
    {
        var palette = EditorPalette.Current();
        _colorizer.Palette = palette;
        _backgroundRenderer.Palette = palette;

        if (this.TryFindResource("Gid.Selection", ActualThemeVariant, out var sel) && sel is IBrush selection)
            Editor.TextArea.SelectionBrush = selection;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionCornerRadius = 3;
        Editor.TextArea.Caret.CaretBrush = palette.Accent;

        MoonIcon.IsVisible = !App.IsDarkTheme;
        SunIcon.IsVisible = App.IsDarkTheme;

        Editor.TextArea.TextView.Redraw();
    }

    private void ApplyEditorFont()
    {
        Editor.FontFamily = _vm.MonospaceEditor ? EditorFonts.Mono : EditorFonts.Sans;
        Editor.FontSize = _vm.EditorFontSize;
        _colorizer.BaseIsMonospace = _vm.MonospaceEditor;
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>Keeps lines at a comfortable reading width by padding the text area when the editor is wide.</summary>
    private void UpdateEditorPadding()
    {
        var width = Editor.Bounds.Width;
        var side = Math.Max(28, (width - EditorMaxTextWidth) / 2);
        Editor.TextArea.TextView.Margin = new Thickness(side, 24, side, 0);
    }

    private void UpdateCaretStatus()
    {
        if (_shownDocument is null)
        {
            _vm.CaretPosition = "";
            return;
        }
        var caret = Editor.TextArea.Caret;
        var sel = Editor.SelectionLength;
        _vm.CaretPosition = sel > 0 ? $"{sel} selected" : $"Ln {caret.Line}, Col {caret.Column}";
    }

    // ================================================================ View model → view

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ActiveDocument):
                ShowDocument(_vm.ActiveDocument);
                break;
            case nameof(MainViewModel.ViewMode):
                ApplyViewMode();
                break;
            case nameof(MainViewModel.IsSidebarVisible):
                ApplySidebarLayout();
                break;
            case nameof(MainViewModel.MonospaceEditor):
            case nameof(MainViewModel.EditorFontSize):
                ApplyEditorFont();
                break;
        }
    }

    private void ShowDocument(DocumentViewModel? doc)
    {
        if (_shownDocument != null)
        {
            _shownDocument.CaretOffset = Editor.CaretOffset;
            _shownDocument.VerticalOffset = Editor.TextArea.TextView.ScrollOffset.Y;
        }

        if (_scanner != null) _scanner.StructureChanged -= OnScannerStructureChanged;
        _shownDocument = doc;

        if (doc is null)
        {
            _scanner = null;
            _colorizer.Scanner = _backgroundRenderer.Scanner = null;
            Editor.Document = _emptyDocument;
            Editor.IsReadOnly = true;
            UpdateCaretStatus();
            return;
        }

        _scanner = new MarkdownBlockScanner(doc.Document);
        _scanner.StructureChanged += OnScannerStructureChanged;
        _colorizer.Scanner = _backgroundRenderer.Scanner = _scanner;

        Editor.Document = doc.Document;
        Editor.IsReadOnly = false;
        Editor.CaretOffset = Math.Clamp(doc.CaretOffset, 0, doc.Document.TextLength);
        var offset = doc.VerticalOffset;
        Dispatcher.UIThread.Post(() =>
        {
            Editor.ScrollToVerticalOffset(offset);
            if (_vm.ViewMode != ViewMode.Preview) Editor.TextArea.Focus();
        }, DispatcherPriority.Loaded);
        UpdateCaretStatus();
    }

    private void OnScannerStructureChanged(object? sender, EventArgs e) =>
        // A code fence opened or closed: lines far from the edit change appearance.
        Dispatcher.UIThread.Post(() => Editor.TextArea.TextView.Redraw(), DispatcherPriority.Render);

    private void ApplyViewMode()
    {
        var cols = EditorArea.ColumnDefinitions;
        var mode = _vm.ViewMode;
        var showEditor = mode != ViewMode.Preview;
        var showPreview = mode != ViewMode.Editor;

        cols[0].Width = showEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        cols[1].Width = GridLength.Auto;
        cols[2].Width = showPreview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Editor.IsVisible = showEditor;
        EditorSplitter.IsVisible = showEditor && showPreview;
        Preview.IsVisible = showPreview;

        if (showEditor && _shownDocument != null)
            Dispatcher.UIThread.Post(() => Editor.TextArea.Focus(), DispatcherPriority.Loaded);
        if (mode == ViewMode.Split)
            Dispatcher.UIThread.Post(SyncPreviewScroll, DispatcherPriority.Background);
    }

    private void ApplySidebarLayout()
    {
        var col = BodyGrid.ColumnDefinitions[0];
        if (_vm.IsSidebarVisible)
        {
            col.MinWidth = 180;
            col.MaxWidth = 520;
            col.Width = new GridLength(Math.Clamp(_vm.Settings.SidebarWidth, 180, 520));
        }
        else
        {
            if (col.Width.Value > 0) _vm.Settings.SidebarWidth = col.Width.Value;
            col.MinWidth = 0;
            col.Width = new GridLength(0);
        }
    }

    // ================================================================ Scroll sync

    private void SyncPreviewScroll()
    {
        if (_vm.ViewMode != ViewMode.Split || _shownDocument is null) return;
        var tv = Editor.TextArea.TextView;
        if (!tv.VisualLinesValid || tv.Document is null) return;

        var y = tv.ScrollOffset.Y;
        var line = tv.GetDocumentLineByVisualTop(y);
        if (line is null) return;
        var top = tv.GetVisualTopByDocumentLine(line.LineNumber);
        var next = line.LineNumber < tv.Document.LineCount
            ? tv.GetVisualTopByDocumentLine(line.LineNumber + 1)
            : top + tv.DefaultLineHeight;
        var fraction = next > top ? (y - top) / (next - top) : 0;
        var atEnd = y > 0 && y + tv.Bounds.Height >= tv.DocumentHeight - 4;
        Preview.ScrollToSourceLine(line.LineNumber - 1 + Math.Clamp(fraction, 0, 1), atEnd);
    }

    // ================================================================ Formatting

    private void OnFormatClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string action }) Format(action);
    }

    private void Format(string action)
    {
        if (_shownDocument is null || !_vm.CanFormat) return;
        switch (action)
        {
            case "bold": MarkdownFormatter.ToggleInline(Editor, "**"); break;
            case "italic": MarkdownFormatter.ToggleInline(Editor, "*"); break;
            case "strike": MarkdownFormatter.ToggleInline(Editor, "~~"); break;
            case "code": MarkdownFormatter.ToggleInline(Editor, "`"); break;
            case "h1": MarkdownFormatter.ToggleHeading(Editor, 1); break;
            case "h2": MarkdownFormatter.ToggleHeading(Editor, 2); break;
            case "h3": MarkdownFormatter.ToggleHeading(Editor, 3); break;
            case "h4": MarkdownFormatter.ToggleHeading(Editor, 4); break;
            case "h5": MarkdownFormatter.ToggleHeading(Editor, 5); break;
            case "h6": MarkdownFormatter.ToggleHeading(Editor, 6); break;
            case "bullet": MarkdownFormatter.ToggleLinePrefix(Editor, LinePrefixKind.Bullet); break;
            case "number": MarkdownFormatter.ToggleLinePrefix(Editor, LinePrefixKind.Numbered); break;
            case "task": MarkdownFormatter.ToggleLinePrefix(Editor, LinePrefixKind.Task); break;
            case "quote": MarkdownFormatter.ToggleLinePrefix(Editor, LinePrefixKind.Quote); break;
            case "link": MarkdownFormatter.InsertLink(Editor); break;
            case "table": MarkdownFormatter.InsertTable(Editor); break;
            case "codeblock": MarkdownFormatter.InsertCodeBlock(Editor); break;
            case "rule": MarkdownFormatter.InsertHorizontalRule(Editor); break;
            case "image": _ = InsertImageFromPickerAsync(); return;
        }
        Editor.TextArea.Focus();
    }

    private async Task InsertImageFromPickerAsync()
    {
        var files = await PickImagesAsync();
        foreach (var file in files) InsertImageFile(file, Editor.CaretOffset);
        Editor.TextArea.Focus();
    }

    private void InsertImageFile(string path, int offset)
    {
        var link = _vm.ImportImageFile(path);
        if (link is null) return;
        Editor.Select(offset, 0);
        Editor.CaretOffset = offset;
        MarkdownFormatter.InsertImageLink(Editor, Path.GetFileNameWithoutExtension(path), link);
    }

    // ================================================================ Keyboard

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shownDocument is null) return;
        var mods = e.KeyModifiers;

        if (e.Key == Key.Enter && mods == KeyModifiers.None)
        {
            if (MarkdownFormatter.HandleEnter(Editor)) e.Handled = true;
        }
        else if (e.Key == Key.Tab && (mods == KeyModifiers.None || mods == KeyModifiers.Shift))
        {
            if (MarkdownFormatter.HandleTab(Editor, outdent: mods == KeyModifiers.Shift)) e.Handled = true;
        }
        else if (e.Key == Key.V && mods == _primary)
        {
            e.Handled = true;
            _ = PasteAsync();
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        var primary = mods.HasFlag(_primary);
        var shift = mods.HasFlag(KeyModifiers.Shift);
        var alt = mods.HasFlag(KeyModifiers.Alt);
        if (alt) return;

        // Ctrl+Tab cycles tabs on every platform, including macOS.
        if (e.Key == Key.Tab && mods.HasFlag(KeyModifiers.Control))
        {
            _vm.NextTabCommand.Execute(shift ? "-1" : "1");
            e.Handled = true;
            return;
        }

        if (!primary) return;

        string? format = null;
        Action? action = null;

        if (!shift)
        {
            switch (e.Key)
            {
                case Key.N: action = () => _vm.NewNoteCommand.Execute(null); break;
                case Key.O: action = () => _vm.OpenFolderCommand.Execute(null); break;
                case Key.S: action = () => _vm.SaveCommand.Execute(null); break;
                case Key.W: action = () => _vm.CloseDocumentCommand.Execute(null); break;
                case Key.E: action = CycleViewMode; break;
                case Key.OemBackslash or Key.Oem5: action = () => _vm.ToggleSidebarCommand.Execute(null); break;
                case Key.OemPlus or Key.Add: action = () => _vm.ChangeFontSizeCommand.Execute("1"); break;
                case Key.OemMinus or Key.Subtract: action = () => _vm.ChangeFontSizeCommand.Execute("-1"); break;
                case Key.D0 or Key.NumPad0: action = () => _vm.ChangeFontSizeCommand.Execute("0"); break;
                case Key.B: format = "bold"; break;
                case Key.I: format = "italic"; break;
                case Key.K: format = "link"; break;
                case Key.OemTilde: format = "code"; break;
                case Key.D1: format = "h1"; break;
                case Key.D2: format = "h2"; break;
                case Key.D3: format = "h3"; break;
                case Key.D4: format = "h4"; break;
                case Key.D5: format = "h5"; break;
                case Key.D6: format = "h6"; break;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.F: action = () => _vm.ShowSearchCommand.Execute(null); break;
                case Key.E: action = () => _vm.ShowExplorerCommand.Execute(null); break;
                case Key.X: format = "strike"; break;
                case Key.C when Editor.TextArea.IsKeyboardFocusWithin: format = "codeblock"; break;
                case Key.D7: format = "number"; break;
                case Key.D8: format = "bullet"; break;
                case Key.D9: format = "task"; break;
                case Key.OemPeriod: format = "quote"; break;
            }
        }

        if (format != null)
        {
            // Formatting shortcuts only apply while typing in the note, not in the search box or tree.
            if (_vm.CanFormat && Editor.TextArea.IsKeyboardFocusWithin)
            {
                Format(format);
                e.Handled = true;
            }
        }
        else if (action != null)
        {
            action();
            e.Handled = true;
        }
    }

    private void CycleViewMode() =>
        _vm.ViewMode = _vm.ViewMode switch
        {
            ViewMode.Editor => ViewMode.Split,
            ViewMode.Split => ViewMode.Preview,
            _ => ViewMode.Editor,
        };

    private void SetupShortcutHints()
    {
        if (!OperatingSystem.IsMacOS()) return;
        HintNew.Text = "⌘N";
        HintSearch.Text = "⇧⌘F";
        HintView.Text = "⌘E";
        HintSidebar.Text = "⌘\\";
    }

    // ================================================================ Paste & drop

    private async Task PasteAsync()
    {
        if (Clipboard is not { } clipboard || _shownDocument is null) return;

        try
        {
            // Image files copied in a file manager.
            var files = await clipboard.TryGetFilesAsync();
            var imagePaths = files?.Select(f => f.TryGetLocalPath()).Where(p => p != null && WorkspaceFiles.IsImage(p)).ToList();
            if (imagePaths is { Count: > 0 })
            {
                foreach (var p in imagePaths) InsertImageFile(p!, Editor.CaretOffset);
                return;
            }

            // Screenshots and images copied from browsers or image editors.
            var formats = await clipboard.GetDataFormatsAsync();
            if (formats.Contains(DataFormat.Bitmap) && !formats.Contains(DataFormat.Text))
            {
                using var bitmap = await clipboard.TryGetBitmapAsync();
                if (bitmap != null)
                {
                    var link = _vm.ImportImageData(s => bitmap.Save(s, new Avalonia.Media.Imaging.PngBitmapEncoderOptions()), ".png");
                    if (link != null) MarkdownFormatter.InsertImageLink(Editor, "image", link);
                    return;
                }
            }

            // Pasting a URL over selected text turns the selection into a link.
            if (Editor.SelectionLength > 0 && formats.Contains(DataFormat.Text))
            {
                var text = (await clipboard.TryGetTextAsync())?.Trim();
                if (text != null && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
                {
                    var start = Editor.SelectionStart;
                    var label = Editor.SelectedText;
                    var replacement = $"[{label}]({text})";
                    Editor.Document.Replace(start, Editor.SelectionLength, replacement);
                    Editor.Select(start + replacement.Length, 0);
                    Editor.CaretOffset = start + replacement.Length;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clipboard read failed: {ex}");
        }

        Editor.Paste();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is null || paths.Count == 0) return;
        e.Handled = true;

        // A dropped folder becomes the workspace.
        if (paths.Count == 1 && Directory.Exists(paths[0]))
        {
            _vm.OpenWorkspace(paths[0], restoreTabs: false);
            return;
        }

        var images = paths.Where(WorkspaceFiles.IsImage).ToList();
        if (images.Count > 0 && _shownDocument != null && _vm.CanFormat)
        {
            var offset = Editor.CaretOffset;
            if (Editor.IsPointerOver && Editor.GetPositionFromPoint(e.GetPosition(Editor)) is { } pos)
                offset = Editor.Document.GetOffset(pos.Location);
            foreach (var image in images)
            {
                InsertImageFile(image, offset);
                offset = Editor.CaretOffset;
            }
            Editor.TextArea.Focus();
        }

        foreach (var note in paths.Where(WorkspaceFiles.IsNote)) _vm.OpenFile(note);
    }

    // ================================================================ Navigation, tree, preview

    private void NavigateTo(int line, int column, int length)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_shownDocument is null) return;
            var doc = Editor.Document;
            line = Math.Clamp(line, 1, doc.LineCount);
            var docLine = doc.GetLineByNumber(line);
            var offset = docLine.Offset + Math.Clamp(column - 1, 0, docLine.Length);
            length = Math.Clamp(length, 0, docLine.EndOffset - offset);
            Editor.Select(offset, length);
            Editor.CaretOffset = offset + length;
            Editor.ScrollTo(line, column);
            if (_vm.ViewMode == ViewMode.Preview) _vm.ViewMode = ViewMode.Split;
            Editor.TextArea.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void OnFileTreeTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Control { DataContext: FileNodeViewModel node } source) return;
        // The expander chevron toggles folders by itself; a click anywhere else on the row does it too.
        if (node.IsDirectory && source.FindAncestorOfType<ToggleButton>(includeSelf: true) != null) return;
        _vm.OpenNodeCommand.Execute(node);
    }

    private void OnFileTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm.SelectedNode is not { } node) return;
        switch (e.Key)
        {
            case Key.Enter when node.IsFile:
                _vm.OpenNodeCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.F2:
                _vm.RenameCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Delete:
            case Key.Back when OperatingSystem.IsMacOS() && e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                _vm.DeleteCommand.Execute(node);
                e.Handled = true;
                break;
        }
    }

    private static readonly Regex TaskBox = new(@"^\s*(?:>\s*)*[-*+][ \t]+(\[[ xX]\])", RegexOptions.Compiled);

    private void OnPreviewTaskToggled(int sourceLine)
    {
        if (_shownDocument is null) return;
        var doc = _shownDocument.Document;
        if (sourceLine < 0 || sourceLine >= doc.LineCount) return;
        var line = doc.GetLineByNumber(sourceLine + 1);
        var match = TaskBox.Match(doc.GetText(line));
        if (match.Success) MarkdownFormatter.ToggleTaskAt(doc, line.Offset + match.Groups[1].Index);
    }

    // ================================================================ Window lifecycle

    private void RestoreWindowBounds()
    {
        var s = _vm.Settings;
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed) return;

        e.Cancel = true;
        var s = _vm.Settings;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Bounds.Width;
            s.WindowHeight = Bounds.Height;
        }
        if (_vm.IsSidebarVisible) s.SidebarWidth = BodyGrid.ColumnDefinitions[0].ActualWidth;

        if (await _vm.PrepareToCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ================================================================ IUiServices

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> PickImagesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Insert image",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.svg"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public Task<string?> PromptAsync(string title, string message, string initialValue, string okText = "OK") =>
        DialogWindow.PromptAsync(this, title, message, initialValue, okText);

    public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> buttons, int destructiveIndex = -1) =>
        DialogWindow.ChooseAsync(this, title, message, buttons, destructiveIndex);

    public void OpenExternal(string pathOrUrl)
    {
        try
        {
            if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && !uri.IsFile)
                _ = Launcher.LaunchUriAsync(uri);
            else if (Directory.Exists(pathOrUrl))
                _ = Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(pathOrUrl));
            else
                _ = Launcher.LaunchFileInfoAsync(new FileInfo(pathOrUrl));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not open {pathOrUrl}: {ex}");
        }
    }

    public void RevealInFileManager(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { File.Exists(path) ? $"/select,{path}" : path } });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", path } });
            }
            else
            {
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
                _ = Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not reveal {path}: {ex}");
        }
    }
}
