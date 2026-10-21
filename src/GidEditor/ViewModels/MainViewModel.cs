using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GidEditor.Models;
using GidEditor.Services;

namespace GidEditor.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private const int MaxRecentWorkspaces = 8;

    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _fsTimer;
    private readonly DispatcherTimer _searchTimer;
    private readonly object _fsLock = new();
    private IUiServices _ui = null!;
    private FileSystemWatcher? _watcher;
    private bool _fsTreeChanged;
    private bool _fsAnyChange;
    private CancellationTokenSource? _searchCts;

    // Settings writes are suppressed until startup restore finishes, so half-loaded state is never persisted.
    private bool _suppressSave = true;

    public MainViewModel() : this(new SettingsService())
    {
    }

    public MainViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Load();

        ViewMode = _settings.ViewMode;
        Theme = _settings.Theme;
        Autosave = _settings.Autosave;
        MonospaceEditor = _settings.MonospaceEditor;
        IsSidebarVisible = _settings.SidebarVisible;
        EditorFontSize = Math.Clamp(_settings.EditorFontSize, 10, 32);
        foreach (var r in _settings.RecentWorkspaces.Where(Directory.Exists)) RecentWorkspaces.Add(r);
        RecentWorkspaces.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentWorkspaces));

        _fsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _fsTimer.Tick += (_, _) => ProcessFileSystemChanges();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await SearchNowAsync();
        };
    }

    public AppSettings Settings => _settings;

    public void Attach(IUiServices ui) => _ui = ui;

    /// <summary>Reopens the last workspace and its tabs.</summary>
    public void Restore()
    {
        try
        {
            if (_settings.WorkspacePath is { } path && Directory.Exists(path))
                OpenWorkspace(path, restoreTabs: true);
        }
        finally
        {
            _suppressSave = false;
            SaveSettings();
        }
    }

    // ---------------------------------------------------------------- Workspace

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkspace), nameof(WorkspaceName), nameof(WindowTitle))]
    public partial string? WorkspacePath { get; private set; }

    public bool HasWorkspace => WorkspacePath != null;

    public string WorkspaceName => WorkspacePath is null ? "" : Path.GetFileName(WorkspacePath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : WorkspacePath;

    [ObservableProperty]
    public partial FileNodeViewModel? Root { get; private set; }

    [ObservableProperty]
    public partial FileNodeViewModel? SelectedNode { get; set; }

    public ObservableCollection<string> RecentWorkspaces { get; } = [];

    public bool HasRecentWorkspaces => RecentWorkspaces.Count > 0;

    [RelayCommand]
    private async Task OpenFolder()
    {
        var path = await _ui.PickFolderAsync("Open a folder as workspace");
        if (path != null) OpenWorkspace(path, restoreTabs: false);
    }

    [RelayCommand]
    private void OpenRecent(string path)
    {
        if (Directory.Exists(path))
        {
            OpenWorkspace(path, restoreTabs: false);
        }
        else
        {
            RecentWorkspaces.Remove(path);
            SaveSettings();
        }
    }

    public void OpenWorkspace(string path, bool restoreTabs)
    {
        path = Path.GetFullPath(path);
        if (WorkspacePath != null && WorkspaceFiles.PathsEqual(WorkspacePath, path)) return;

        SaveAll();
        foreach (var doc in Documents.ToList()) RemoveDocument(doc);

        WorkspacePath = path;
        var root = new FileNodeViewModel(this, path, isDirectory: true, parent: null) { IsExpanded = true };
        root.SyncChildren();
        Root = root;
        StartWatcher(path);

        RecentWorkspaces.Remove(RecentWorkspaces.FirstOrDefault(r => WorkspaceFiles.PathsEqual(r, path)) ?? "");
        RecentWorkspaces.Insert(0, path);
        while (RecentWorkspaces.Count > MaxRecentWorkspaces) RecentWorkspaces.RemoveAt(RecentWorkspaces.Count - 1);

        if (restoreTabs)
        {
            foreach (var file in _settings.OpenFiles.Where(File.Exists))
                OpenFile(file, activate: false);
            var active = Documents.FirstOrDefault(d => _settings.ActiveFile != null && WorkspaceFiles.PathsEqual(d.FilePath, _settings.ActiveFile));
            ActiveDocument = active ?? Documents.FirstOrDefault();
        }

        SearchQuery = "";
        SearchResults.Clear();
        SaveSettings();
    }

    [RelayCommand]
    private void CloseWorkspace()
    {
        SaveAll();
        foreach (var doc in Documents.ToList()) RemoveDocument(doc);
        StopWatcher();
        Root = null;
        WorkspacePath = null;
        SaveSettings();
    }

    [RelayCommand]
    private void RefreshTree() => Root?.SyncChildren();

    [RelayCommand]
    private void CollapseAll()
    {
        if (Root is null) return;
        foreach (var n in Root.Descendants()) n.IsExpanded = false;
    }

    /// <summary>
    /// Folder that new notes go into: the given node, else the selected folder or the selected note's folder,
    /// else the workspace root (nothing selected).
    /// </summary>
    private string TargetFolder(FileNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node != null) return node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath)!;
        return WorkspacePath!;
    }

    [RelayCommand]
    private void ClearSelection() => SelectedNode = null;

    [RelayCommand]
    private async Task NewNote(FileNodeViewModel? parent)
    {
        if (!HasWorkspace)
        {
            await OpenFolder();
            if (!HasWorkspace) return;
        }

        var folder = TargetFolder(parent);
        var name = await _ui.PromptAsync("New note", $"Create a note in “{RelativeName(folder)}”", "Untitled", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        name = WorkspaceFiles.SanitizeFileName(name);
        var fileName = WorkspaceFiles.IsNote(name) ? name : name + ".md";
        var path = WorkspaceFiles.UniquePath(Path.Combine(folder, fileName));
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(path, $"# {Path.GetFileNameWithoutExtension(path)}\n\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowError("Could not create the note", ex.Message);
            return;
        }

        RefreshAndReveal(path);
        OpenFile(path);
        // Put the caret below the title, ready to type.
        NavigateRequested?.Invoke(3, 1, 0);
    }

    [RelayCommand]
    private async Task NewFolder(FileNodeViewModel? parent)
    {
        if (!HasWorkspace) return;
        var folder = TargetFolder(parent);
        var name = await _ui.PromptAsync("New folder", $"Create a folder in “{RelativeName(folder)}”", "New folder", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;

        var path = WorkspaceFiles.UniquePath(Path.Combine(folder, WorkspaceFiles.SanitizeFileName(name)));
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowError("Could not create the folder", ex.Message);
            return;
        }
        RefreshAndReveal(path);
    }

    [RelayCommand]
    private async Task Rename(FileNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null || node == Root) return;

        var isMd = node.IsFile && Path.GetExtension(node.FullPath).Equals(".md", StringComparison.OrdinalIgnoreCase);
        var current = isMd ? Path.GetFileNameWithoutExtension(node.FullPath) : node.Name;
        var name = await _ui.PromptAsync(node.IsDirectory ? "Rename folder" : "Rename note", "New name", current, "Rename");
        if (string.IsNullOrWhiteSpace(name) || name == current) return;

        name = WorkspaceFiles.SanitizeFileName(name);
        if (isMd && !WorkspaceFiles.IsNote(name)) name += ".md";
        var oldPath = node.FullPath;
        var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, name);

        // Case-only renames are legal on case-insensitive file systems; only reject real collisions.
        if (!WorkspaceFiles.PathsEqual(oldPath, newPath) && (File.Exists(newPath) || Directory.Exists(newPath)))
        {
            await ShowError("Name already in use", $"“{name}” already exists in this folder.");
            return;
        }

        try
        {
            foreach (var d in DocumentsUnder(oldPath)) d.Save();
            if (node.IsDirectory) Directory.Move(oldPath, newPath);
            else File.Move(oldPath, newPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowError("Could not rename", ex.Message);
            return;
        }

        foreach (var d in DocumentsUnder(oldPath))
            d.UpdatePath(newPath + d.FilePath[oldPath.Length..]);
        OnPropertyChanged(nameof(WindowTitle));
        RefreshAndReveal(newPath);
    }

    [RelayCommand]
    private async Task Delete(FileNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null || node == Root) return;

        var what = node.IsDirectory ? $"the folder “{node.Name}” and everything in it" : $"“{node.DisplayName}”";
        var choice = await _ui.ChooseAsync("Delete", $"Move {what} to the trash?", ["Move to Trash", "Cancel"], destructiveIndex: 0);
        if (choice != 0) return;

        var docs = DocumentsUnder(node.FullPath).ToList();
        foreach (var d in docs)
        {
            d.StopTimers();
            RemoveDocument(d);
        }

        if (!TrashService.TryMoveToTrash(node.FullPath))
        {
            var permanent = await _ui.ChooseAsync("Trash unavailable",
                $"{node.Name} could not be moved to the trash. Delete it permanently? This cannot be undone.",
                ["Delete Permanently", "Cancel"], destructiveIndex: 0);
            if (permanent != 0)
            {
                foreach (var d in docs) OpenFile(d.FilePath, activate: false);
                return;
            }
            try
            {
                TrashService.DeletePermanently(node.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await ShowError("Could not delete", ex.Message);
            }
        }
        Root?.SyncChildren();
    }

    [RelayCommand]
    private void Reveal(FileNodeViewModel? node)
    {
        var path = node?.FullPath ?? ActiveDocument?.FilePath ?? WorkspacePath;
        if (path != null) _ui.RevealInFileManager(path);
    }

    [RelayCommand]
    private void OpenNode(FileNodeViewModel? node)
    {
        if (node is null) return;
        if (node.IsDirectory) node.IsExpanded = !node.IsExpanded;
        else OpenFile(node.FullPath);
    }

    private void RefreshAndReveal(string path)
    {
        Root?.SyncChildren();
        SelectedNode = FindNode(path, expand: true);
    }

    public FileNodeViewModel? FindNode(string path, bool expand)
    {
        if (Root is null || !WorkspaceFiles.IsUnder(Root.FullPath, path)) return null;
        var node = Root;
        var rel = Path.GetRelativePath(Root.FullPath, path);
        foreach (var part in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (expand) node.IsExpanded = true;
            var next = node.Children.FirstOrDefault(c => c.Name == part);
            if (next is null) return null;
            node = next;
        }
        return node;
    }

    private string RelativeName(string folder) =>
        WorkspacePath is null || WorkspaceFiles.PathsEqual(folder, WorkspacePath)
            ? WorkspaceName
            : Path.GetRelativePath(WorkspacePath, folder);

    // ---------------------------------------------------------------- Watching the folder

    private void StartWatcher(string path)
    {
        StopWatcher();
        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Changed += (_, e) => OnFsEvent(e.FullPath, structural: false);
            _watcher.Created += (_, e) => OnFsEvent(e.FullPath, structural: true);
            _watcher.Deleted += (_, e) => OnFsEvent(e.FullPath, structural: true);
            _watcher.Renamed += (_, e) => OnFsEvent(e.FullPath, structural: true);
            // Buffer overflow (e.g. a big sync): rescan everything.
            _watcher.Error += (_, _) => OnFsEvent(path, structural: true);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // Watching is best effort (e.g. inotify limits on Linux); the editor still works without it.
            _watcher = null;
        }
    }

    private void StopWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnFsEvent(string path, bool structural)
    {
        if (WorkspacePath is null) return;
        if (!WorkspaceFiles.PathsEqual(path, WorkspacePath) && WorkspaceFiles.IsInIgnoredLocation(WorkspacePath, path)) return;
        lock (_fsLock)
        {
            _fsAnyChange = true;
            _fsTreeChanged |= structural;
        }
        Dispatcher.UIThread.Post(() =>
        {
            _fsTimer.Stop();
            _fsTimer.Start();
        });
    }

    private void ProcessFileSystemChanges()
    {
        _fsTimer.Stop();
        bool tree, any;
        lock (_fsLock)
        {
            tree = _fsTreeChanged;
            any = _fsAnyChange;
            _fsTreeChanged = _fsAnyChange = false;
        }
        if (!any) return;
        if (tree) Root?.SyncChildren();
        foreach (var doc in Documents) doc.CheckDisk();
    }

    // ---------------------------------------------------------------- Documents (tabs)

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveDocument), nameof(WindowTitle), nameof(CanFormat))]
    public partial DocumentViewModel? ActiveDocument { get; set; }

    public bool HasActiveDocument => ActiveDocument != null;

    /// <summary>Formatting tools only make sense when a note is open and its source is visible.</summary>
    public bool CanFormat => HasActiveDocument && !IsPreviewMode;

    public string WindowTitle => ActiveDocument != null
        ? $"{ActiveDocument.Title} — {WorkspaceName} — GidEditor"
        : HasWorkspace ? $"{WorkspaceName} — GidEditor" : "GidEditor";

    /// <summary>Asks the view to move the caret to a 1-based line/column and select <c>length</c> characters.</summary>
    public event Action<int, int, int>? NavigateRequested;

    partial void OnActiveDocumentChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        oldValue?.FlushPendingSave();
        if (newValue != null && FindNode(newValue.FilePath, expand: true) is { } node) SelectedNode = node;
        SaveSettings();
    }

    public DocumentViewModel? OpenFile(string path, bool activate = true)
    {
        var existing = Documents.FirstOrDefault(d => WorkspaceFiles.PathsEqual(d.FilePath, path));
        if (existing != null)
        {
            if (activate) ActiveDocument = existing;
            return existing;
        }

        DocumentViewModel doc;
        try
        {
            doc = DocumentViewModel.Load(path, () => Autosave);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ShowError("Could not open the note", ex.Message);
            return null;
        }

        doc.CloseRequested += (_, _) => _ = CloseDocument(doc);
        var index = ActiveDocument != null ? Documents.IndexOf(ActiveDocument) + 1 : Documents.Count;
        Documents.Insert(Math.Clamp(index, 0, Documents.Count), doc);
        if (activate) ActiveDocument = doc;
        return doc;
    }

    [RelayCommand]
    private async Task CloseDocument(DocumentViewModel? doc)
    {
        doc ??= ActiveDocument;
        if (doc is null) return;

        if (doc.IsDirty && !Autosave)
        {
            var choice = await _ui.ChooseAsync("Unsaved changes", $"Save changes to “{doc.Title}” before closing?",
                ["Save", "Don't Save", "Cancel"], destructiveIndex: 1);
            if (choice is 2 or -1) return;
            if (choice == 0 && !doc.Save())
            {
                await ShowError("Could not save", doc.SaveError ?? "");
                return;
            }
        }
        else if (!doc.Save())
        {
            var choice = await _ui.ChooseAsync("Could not save", $"“{doc.Title}” could not be saved: {doc.SaveError}\n\nClose anyway and lose the changes?",
                ["Close Anyway", "Cancel"], destructiveIndex: 0);
            if (choice != 0) return;
        }

        RemoveDocument(doc);
    }

    private void RemoveDocument(DocumentViewModel doc)
    {
        var index = Documents.IndexOf(doc);
        if (index < 0) return;
        doc.StopTimers();
        Documents.RemoveAt(index);
        if (ActiveDocument == doc)
            ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Min(index, Documents.Count - 1)];
        SaveSettings();
    }

    [RelayCommand]
    private void NextTab(string? direction)
    {
        if (Documents.Count < 2 || ActiveDocument is null) return;
        var step = direction == "-1" ? -1 : 1;
        var i = (Documents.IndexOf(ActiveDocument) + step + Documents.Count) % Documents.Count;
        ActiveDocument = Documents[i];
    }

    [RelayCommand]
    private async Task Save()
    {
        if (ActiveDocument is { } doc && !doc.Save())
            await ShowError("Could not save", doc.SaveError ?? "");
    }

    public void SaveAll()
    {
        foreach (var d in Documents) d.Save();
    }

    private IEnumerable<DocumentViewModel> DocumentsUnder(string path) =>
        Documents.Where(d => WorkspaceFiles.PathsEqual(d.FilePath, path) || WorkspaceFiles.IsUnder(path, d.FilePath)).ToList();

    /// <summary>Called when the window is about to close. Returns false to keep it open.</summary>
    public async Task<bool> PrepareToCloseAsync()
    {
        var dirty = Documents.Where(d => d.IsDirty).ToList();
        if (dirty.Count > 0 && !Autosave)
        {
            var names = string.Join(", ", dirty.Select(d => $"“{d.Title}”"));
            var choice = await _ui.ChooseAsync("Unsaved changes", $"Save changes to {names}?",
                ["Save All", "Don't Save", "Cancel"], destructiveIndex: 1);
            if (choice is 2 or -1) return false;
            if (choice == 1)
            {
                foreach (var d in dirty) d.StopTimers();
                SaveSettings();
                StopWatcher();
                return true;
            }
        }

        var failed = Documents.Where(d => !d.Save()).ToList();
        if (failed.Count > 0)
        {
            var choice = await _ui.ChooseAsync("Could not save",
                $"Some notes could not be saved ({string.Join(", ", failed.Select(d => d.Title))}). Quit anyway?",
                ["Quit Anyway", "Cancel"], destructiveIndex: 0);
            if (choice != 0) return false;
        }

        foreach (var d in Documents) d.StopTimers();
        SaveSettings();
        StopWatcher();
        return true;
    }

    // ---------------------------------------------------------------- Images

    /// <summary>
    /// Makes an image available to the active note: images inside the workspace are linked where they are,
    /// anything else is copied into the workspace's attachments folder. Returns the relative link, or null.
    /// </summary>
    public string? ImportImageFile(string sourcePath)
    {
        if (ActiveDocument is not { } doc) return null;
        if (WorkspacePath != null && WorkspaceFiles.IsUnder(WorkspacePath, sourcePath))
            return WorkspaceFiles.RelativeLink(doc.FilePath, sourcePath);

        var target = AttachmentPath(Path.GetFileName(sourcePath));
        try
        {
            File.Copy(sourcePath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ShowError("Could not copy the image", ex.Message);
            return null;
        }
        return WorkspaceFiles.RelativeLink(doc.FilePath, target);
    }

    /// <summary>Saves image data (e.g. a pasted screenshot) into the attachments folder and returns its relative link.</summary>
    public string? ImportImageData(Action<Stream> write, string extension)
    {
        if (ActiveDocument is not { } doc) return null;
        var name = $"{WorkspaceFiles.SanitizeFileName(doc.Title)}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}";
        var target = AttachmentPath(name);
        try
        {
            using (var stream = File.Create(target)) write(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ShowError("Could not save the image", ex.Message);
            return null;
        }
        return WorkspaceFiles.RelativeLink(doc.FilePath, target);
    }

    private string AttachmentPath(string fileName)
    {
        var root = WorkspacePath ?? ActiveDocument!.DirectoryPath;
        var dir = Path.Combine(root, WorkspaceFiles.AttachmentsFolder);
        Directory.CreateDirectory(dir);
        return WorkspaceFiles.UniquePath(Path.Combine(dir, WorkspaceFiles.SanitizeFileName(fileName)));
    }

    /// <summary>Handles a click on a link in the preview.</summary>
    public void OpenLink(string url)
    {
        if (ImageLoaderIsExternal(url))
        {
            _ui.OpenExternal(url);
            return;
        }
        var path = Markdown.ImageLoader.ResolveLocalPath(url, ActiveDocument?.DirectoryPath);
        if (path is null) return;
        // Links like [x](other-note) often omit the extension.
        if (!File.Exists(path) && File.Exists(path + ".md")) path += ".md";
        if (File.Exists(path) && WorkspaceFiles.IsNote(path)) OpenFile(path);
        else if (File.Exists(path) || Directory.Exists(path)) _ui.OpenExternal(path);
    }

    private static bool ImageLoaderIsExternal(string url) =>
        url.Contains("://", StringComparison.Ordinal) && !url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- View & preferences

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorMode), nameof(IsSplitMode), nameof(IsPreviewMode), nameof(CanFormat))]
    public partial ViewMode ViewMode { get; set; }

    public bool IsEditorMode => ViewMode == ViewMode.Editor;
    public bool IsSplitMode => ViewMode == ViewMode.Split;
    public bool IsPreviewMode => ViewMode == ViewMode.Preview;

    [RelayCommand]
    private void SetViewMode(string mode)
    {
        if (Enum.TryParse<ViewMode>(mode, out var m)) ViewMode = m;
    }

    partial void OnViewModeChanged(ViewMode value) => SaveSettings();

    [ObservableProperty]
    public partial bool IsSidebarVisible { get; set; }

    partial void OnIsSidebarVisibleChanged(bool value) => SaveSettings();

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExplorerPanel))]
    public partial bool IsSearchPanel { get; set; }

    public bool IsExplorerPanel => !IsSearchPanel;

    public event Action? FocusSearchRequested;

    [RelayCommand]
    private void ShowExplorer()
    {
        IsSearchPanel = false;
        IsSidebarVisible = true;
    }

    [RelayCommand]
    private void ShowSearch()
    {
        IsSearchPanel = true;
        IsSidebarVisible = true;
        FocusSearchRequested?.Invoke();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeLabel))]
    public partial ThemeMode Theme { get; set; }

    public string ThemeLabel => Theme switch
    {
        ThemeMode.Light => "Light",
        ThemeMode.Dark => "Dark",
        _ => "System",
    };

    partial void OnThemeChanged(ThemeMode value)
    {
        App.ApplyTheme(value);
        SaveSettings();
    }

    [RelayCommand]
    private void SetTheme(string mode)
    {
        if (Enum.TryParse<ThemeMode>(mode, out var t)) Theme = t;
    }

    [RelayCommand]
    private void ToggleTheme() =>
        Theme = App.IsDarkTheme ? ThemeMode.Light : ThemeMode.Dark;

    [ObservableProperty]
    public partial bool Autosave { get; set; }

    partial void OnAutosaveChanged(bool value)
    {
        if (value) SaveAll();
        SaveSettings();
    }

    [ObservableProperty]
    public partial bool MonospaceEditor { get; set; }

    partial void OnMonospaceEditorChanged(bool value) => SaveSettings();

    [ObservableProperty]
    public partial double EditorFontSize { get; set; }

    partial void OnEditorFontSizeChanged(double value) => SaveSettings();

    [RelayCommand]
    private void ChangeFontSize(string delta) =>
        EditorFontSize = delta == "0" ? 15 : Math.Clamp(EditorFontSize + double.Parse(delta, System.Globalization.CultureInfo.InvariantCulture), 10, 32);

    [ObservableProperty]
    public partial string CaretPosition { get; set; } = "";

    // ---------------------------------------------------------------- Search

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    public ObservableCollection<SearchFileViewModel> SearchResults { get; } = [];

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    [ObservableProperty]
    public partial string SearchSummary { get; private set; } = "";

    partial void OnSearchQueryChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    /// <summary>Runs the search for <see cref="SearchQuery"/> immediately, skipping the typing debounce.</summary>
    public async Task SearchNowAsync()
    {
        _searchTimer.Stop();
        _searchCts?.Cancel();
        var query = SearchQuery.Trim();
        if (query.Length == 0 || WorkspacePath is null)
        {
            SearchResults.Clear();
            SearchSummary = "";
            IsSearching = false;
            return;
        }

        // Unsaved edits must be on disk for search to see them.
        foreach (var d in Documents) d.FlushPendingSave();

        var cts = _searchCts = new CancellationTokenSource();
        IsSearching = true;
        try
        {
            var results = await SearchService.SearchAsync(WorkspacePath, query, cts.Token);
            if (cts.IsCancellationRequested) return;
            SearchResults.Clear();
            foreach (var r in results) SearchResults.Add(new SearchFileViewModel(this, r));
            var hits = results.Sum(r => r.Matches.Count);
            SearchSummary = results.Count == 0
                ? "No results"
                : $"{hits} {(hits == 1 ? "match" : "matches")} in {results.Count} {(results.Count == 1 ? "note" : "notes")}";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_searchCts == cts) IsSearching = false;
        }
    }

    [RelayCommand]
    private void OpenSearchFile(SearchFileViewModel result) => OpenFile(result.Path);

    [RelayCommand]
    private void OpenSearchMatch(SearchMatchViewModel match)
    {
        if (OpenFile(match.Path) is null) return;
        NavigateRequested?.Invoke(match.Match.LineNumber, match.Match.Column + 1, match.Match.Length);
    }

    // ---------------------------------------------------------------- Helpers

    private Task ShowError(string title, string message) => _ui.ChooseAsync(title, message, ["OK"]);

    public void SaveSettings()
    {
        if (_suppressSave) return;
        _settings.WorkspacePath = WorkspacePath;
        _settings.RecentWorkspaces = RecentWorkspaces.ToList();
        _settings.OpenFiles = Documents.Select(d => d.FilePath).ToList();
        _settings.ActiveFile = ActiveDocument?.FilePath;
        _settings.ViewMode = ViewMode;
        _settings.Theme = Theme;
        _settings.Autosave = Autosave;
        _settings.MonospaceEditor = MonospaceEditor;
        _settings.SidebarVisible = IsSidebarVisible;
        _settings.EditorFontSize = EditorFontSize;
        _settingsService.Save(_settings);
    }
}
