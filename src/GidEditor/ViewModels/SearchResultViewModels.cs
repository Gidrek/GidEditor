using System.Collections.Generic;
using System.IO;
using System.Linq;
using GidEditor.Services;

namespace GidEditor.ViewModels;

public sealed class SearchFileViewModel
{
    public SearchFileViewModel(MainViewModel owner, SearchFileResult result)
    {
        Owner = owner;
        Path = result.Path;
        FileName = System.IO.Path.GetFileNameWithoutExtension(result.Path);
        Folder = System.IO.Path.GetDirectoryName(result.RelativePath) ?? "";
        Matches = result.Matches.Select(m => new SearchMatchViewModel(owner, result.Path, m)).ToList();
    }

    public MainViewModel Owner { get; }
    public string Path { get; }
    public string FileName { get; }
    public string Folder { get; }
    public bool HasFolder => Folder.Length > 0;
    public IReadOnlyList<SearchMatchViewModel> Matches { get; }
    public string CountText => Matches.Count == 1 ? "1" : Matches.Count.ToString();
}

public sealed class SearchMatchViewModel(MainViewModel owner, string path, SearchMatch match)
{
    public MainViewModel Owner { get; } = owner;
    public string Path { get; } = path;
    public SearchMatch Match { get; } = match;
    public string Before => Match.Before;
    public string Text => Match.Match;
    public string After => Match.After;
    public string LineLabel => Match.LineNumber.ToString();
}
