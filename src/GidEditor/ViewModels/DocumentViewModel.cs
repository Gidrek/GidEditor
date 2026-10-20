using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GidEditor.ViewModels;

/// <summary>An open note (one tab). Owns the editable text, autosave and change-on-disk detection.</summary>
public sealed partial class DocumentViewModel : ViewModelBase
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(180);

    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _previewTimer;
    private readonly Func<bool> _autosaveEnabled;
    private Encoding _encoding = new UTF8Encoding(false);
    private string _diskText = "";
    private string? _conflictText;
    private DateTime _diskStamp;
    private bool _suppressDirty;

    private DocumentViewModel(string path, Func<bool> autosaveEnabled)
    {
        FilePath = path;
        _autosaveEnabled = autosaveEnabled;
        Document = new TextDocument();
        _saveTimer = new DispatcherTimer { Interval = AutosaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
        _previewTimer = new DispatcherTimer { Interval = PreviewDelay };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdatePreview();
        };
        Document.TextChanged += OnTextChanged;
    }

    public TextDocument Document { get; }

    public string FilePath { get; private set; }

    public string FileName => Path.GetFileName(FilePath);

    public string Title => Path.GetFileNameWithoutExtension(FilePath);

    public string DirectoryPath => Path.GetDirectoryName(FilePath)!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatus))]
    public partial bool IsDirty { get; private set; }

    /// <summary>The file changed on disk while there were unsaved edits here.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    public partial bool HasConflict { get; private set; }

    /// <summary>The file was deleted or moved outside the editor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    public partial bool IsMissing { get; private set; }

    [ObservableProperty]
    public partial string PreviewText { get; private set; } = "";

    [ObservableProperty]
    public partial int WordCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatus))]
    public partial string? SaveError { get; private set; }

    public string SaveStatus => SaveError != null ? "Save failed" : IsDirty ? "Edited" : "Saved";

    public bool HasBanner => HasConflict || IsMissing;

    /// <summary>Editor view state, restored when switching back to this tab.</summary>
    public int CaretOffset { get; set; }
    public double VerticalOffset { get; set; }

    public event EventHandler? CloseRequested;

    public static DocumentViewModel Load(string path, Func<bool> autosaveEnabled)
    {
        var doc = new DocumentViewModel(path, autosaveEnabled);
        var text = doc.ReadFromDisk();
        doc._suppressDirty = true;
        doc.Document.Text = text;
        doc.Document.UndoStack.ClearAll();
        doc._suppressDirty = false;
        doc.UpdatePreview();
        return doc;
    }

    private string ReadFromDisk()
    {
        using var reader = new StreamReader(FilePath, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        _encoding = reader.CurrentEncoding;
        _diskText = text;
        _diskStamp = File.GetLastWriteTimeUtc(FilePath);
        return text;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        _previewTimer.Start();
        if (_suppressDirty) return;

        IsDirty = Document.Text != _diskText;
        if (IsDirty && _autosaveEnabled() && !IsMissing && !HasConflict)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    private void UpdatePreview()
    {
        PreviewText = Document.Text;
        WordCount = CountWords(PreviewText);
    }

    /// <summary>Writes the note to disk if it has changes. Returns false if writing failed.</summary>
    public bool Save()
    {
        _saveTimer.Stop();
        if (!IsDirty && !IsMissing) return true;
        try
        {
            var text = Document.Text;
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, text, _encoding);
            _diskText = text;
            _diskStamp = File.GetLastWriteTimeUtc(FilePath);
            IsDirty = false;
            IsMissing = false;
            HasConflict = false;
            _conflictText = null;
            SaveError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveError = ex.Message;
            return false;
        }
    }

    /// <summary>Re-checks the file on disk after a file-system notification.</summary>
    public void CheckDisk()
    {
        if (!File.Exists(FilePath))
        {
            // Sync clients sometimes delete-and-recreate; the next notification will clear this.
            IsMissing = true;
            return;
        }

        if (IsMissing)
        {
            IsMissing = false;
        }

        DateTime stamp;
        string text;
        try
        {
            stamp = File.GetLastWriteTimeUtc(FilePath);
            if (stamp == _diskStamp) return;
            using var reader = new StreamReader(FilePath, _encoding, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (IOException)
        {
            return; // Still being written; another notification will follow.
        }

        _diskStamp = stamp;
        if (text == _diskText) return;

        if (!IsDirty)
        {
            ApplyExternalText(text);
        }
        else
        {
            _conflictText = text;
            HasConflict = true;
            _saveTimer.Stop();
        }
    }

    /// <summary>Replaces only the changed middle of the text so the caret and scroll position survive a reload.</summary>
    private void ApplyExternalText(string text)
    {
        var current = Document.Text;
        var prefix = 0;
        var max = Math.Min(current.Length, text.Length);
        while (prefix < max && current[prefix] == text[prefix]) prefix++;
        var suffix = 0;
        while (suffix < max - prefix && current[^(suffix + 1)] == text[^(suffix + 1)]) suffix++;

        _suppressDirty = true;
        Document.Replace(prefix, current.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
        _suppressDirty = false;
        _diskText = text;
        IsDirty = false;
    }

    /// <summary>Conflict resolution: discard local edits and take the version on disk.</summary>
    [RelayCommand]
    private void TakeDiskVersion()
    {
        if (_conflictText != null) ApplyExternalText(_conflictText);
        _conflictText = null;
        HasConflict = false;
    }

    /// <summary>Conflict resolution: keep local edits and overwrite the file.</summary>
    [RelayCommand]
    private void KeepMine()
    {
        HasConflict = false;
        _conflictText = null;
        IsDirty = true;
        Save();
    }

    /// <summary>Missing-file banner: write the note back to its original location.</summary>
    [RelayCommand]
    private void Restore()
    {
        IsMissing = false;
        IsDirty = true;
        Save();
    }

    [RelayCommand]
    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Called after the file (or a parent folder) was renamed from inside the app.</summary>
    public void UpdatePath(string newPath)
    {
        FilePath = newPath;
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DirectoryPath));
        try
        {
            _diskStamp = File.GetLastWriteTimeUtc(newPath);
        }
        catch (IOException)
        {
        }
    }

    public void FlushPendingSave()
    {
        if (_saveTimer.IsEnabled) Save();
    }

    public void StopTimers()
    {
        _saveTimer.Stop();
        _previewTimer.Stop();
    }

    private static int CountWords(string text) => WordRegex().Count(text);

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’_-]*")]
    private static partial Regex WordRegex();
}
