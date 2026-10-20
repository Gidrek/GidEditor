using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GidEditor.Services;

namespace GidEditor.ViewModels;

/// <summary>A folder or note in the workspace tree.</summary>
public sealed partial class FileNodeViewModel : ViewModelBase
{
    public FileNodeViewModel(MainViewModel owner, string fullPath, bool isDirectory, FileNodeViewModel? parent)
    {
        Owner = owner;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Parent = parent;
    }

    /// <summary>Lets item templates and context menus reach workspace commands.</summary>
    public MainViewModel Owner { get; }

    public FileNodeViewModel? Parent { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(DisplayName))]
    public partial string FullPath { get; set; }

    public bool IsDirectory { get; }

    public bool IsFile => !IsDirectory;

    public string Name => Path.GetFileName(FullPath);

    /// <summary>Notes are shown without their ".md" extension, like titles.</summary>
    public string DisplayName => IsDirectory || !FullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        ? Name
        : Path.GetFileNameWithoutExtension(FullPath);

    public ObservableCollection<FileNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Brings <see cref="Children"/> in line with the file system without recreating unchanged nodes,
    /// so expansion state and selection survive refreshes.</summary>
    public void SyncChildren()
    {
        if (!IsDirectory) return;

        List<(string Path, bool IsDir)> entries;
        try
        {
            var dirs = Directory.EnumerateDirectories(FullPath)
                .Where(d => !WorkspaceFiles.IsIgnoredFolder(Path.GetFileName(d)))
                .OrderBy(d => Path.GetFileName(d), NaturalComparer.Instance)
                .Select(d => (d, true));
            var files = Directory.EnumerateFiles(FullPath)
                .Where(WorkspaceFiles.IsNote)
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .OrderBy(f => Path.GetFileName(f), NaturalComparer.Instance)
                .Select(f => (f, false));
            entries = dirs.Concat(files).ToList();
        }
        catch (Exception)
        {
            entries = [];
        }

        var existing = Children.ToDictionary(c => (c.FullPath, c.IsDirectory));
        for (var i = 0; i < entries.Count; i++)
        {
            var (path, isDir) = entries[i];
            if (!existing.Remove((path, isDir), out var node))
                node = new FileNodeViewModel(Owner, path, isDir, this);

            var currentIndex = Children.IndexOf(node);
            if (currentIndex < 0) Children.Insert(i, node);
            else if (currentIndex != i) Children.Move(currentIndex, i);

            if (isDir) node.SyncChildren();
        }

        foreach (var stale in existing.Values) Children.Remove(stale);
    }

    public IEnumerable<FileNodeViewModel> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var d in child.Descendants()) yield return d;
        }
    }
}

/// <summary>Sorts "Note 2" before "Note 10".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                var c = string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
