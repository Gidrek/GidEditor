using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GidEditor.Services;

public sealed record SearchMatch(int LineNumber, int Column, int Length, string Before, string Match, string After);

public sealed record SearchFileResult(string Path, string RelativePath, bool NameMatches, IReadOnlyList<SearchMatch> Matches);

/// <summary>Case-insensitive full-text search over every note in a workspace.</summary>
public static class SearchService
{
    private const int MaxMatchesPerFile = 50;
    private const int MaxTotalMatches = 1000;
    // Little leading context so the match stays visible in the narrow sidebar; more trailing context.
    private const int ContextBefore = 16;
    private const int ContextAfter = 80;

    public static Task<List<SearchFileResult>> SearchAsync(string root, string query, CancellationToken ct) =>
        Task.Run(() => Search(root, query, ct), ct);

    private static List<SearchFileResult> Search(string root, string query, CancellationToken ct)
    {
        var results = new List<SearchFileResult>();
        var total = 0;

        foreach (var file in WorkspaceFiles.EnumerateNotes(root))
        {
            ct.ThrowIfCancellationRequested();
            if (total >= MaxTotalMatches) break;

            var nameMatches = Path.GetFileName(file).Contains(query, StringComparison.OrdinalIgnoreCase);
            var matches = new List<SearchMatch>();
            try
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    var idx = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    while (idx >= 0 && matches.Count < MaxMatchesPerFile)
                    {
                        matches.Add(MakeMatch(line, lineNumber, idx, query.Length));
                        idx = line.IndexOf(query, idx + query.Length, StringComparison.OrdinalIgnoreCase);
                    }
                    if (matches.Count >= MaxMatchesPerFile) break;
                }
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (matches.Count > 0 || nameMatches)
            {
                total += matches.Count;
                results.Add(new SearchFileResult(file, Path.GetRelativePath(root, file), nameMatches, matches));
            }
        }

        results.Sort((a, b) =>
        {
            // Files whose name matches come first, then by number of hits.
            var c = b.NameMatches.CompareTo(a.NameMatches);
            if (c != 0) return c;
            c = b.Matches.Count.CompareTo(a.Matches.Count);
            return c != 0 ? c : string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);
        });
        return results;
    }

    private static SearchMatch MakeMatch(string line, int lineNumber, int idx, int length)
    {
        var start = Math.Max(0, idx - ContextBefore);
        var end = Math.Min(line.Length, idx + length + ContextAfter);
        var before = (start > 0 ? "…" : "") + line[start..idx].TrimStart();
        var after = line[(idx + length)..end] + (end < line.Length ? "…" : "");
        return new SearchMatch(lineNumber, idx, length, before, line.Substring(idx, length), after);
    }
}
