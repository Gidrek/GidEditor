using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GidEditor.Services;

/// <summary>Rules for which files and folders belong to a notes workspace.</summary>
public static class WorkspaceFiles
{
    public const string AttachmentsFolder = "attachments";

    private static readonly HashSet<string> NoteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd", ".txt",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg",
    };

    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "$RECYCLE.BIN", "System Volume Information",
    };

    public static bool IsNote(string path) => NoteExtensions.Contains(Path.GetExtension(path));

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>Hidden folders (.git, .obsidian, .dropbox.cache …) and build folders are skipped.</summary>
    public static bool IsIgnoredFolder(string name) =>
        name.StartsWith('.') || IgnoredFolders.Contains(name);

    /// <summary>True when any segment of <paramref name="path"/> below <paramref name="root"/> is ignored or hidden.</summary>
    public static bool IsInIgnoredLocation(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        if (rel.StartsWith("..", StringComparison.Ordinal)) return true;
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.StartsWith('.') || IgnoredFolders.Contains(p)) ||
               // Sync clients write temp files like "~$note.md" or "note.md.tmp".
               Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal);
    }

    public static IEnumerable<string> EnumerateNotes(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> files, dirs;
            try
            {
                files = Directory.EnumerateFiles(dir);
                dirs = Directory.EnumerateDirectories(dir);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var f in files)
                if (IsNote(f)) yield return f;
            foreach (var d in dirs)
                if (!IsIgnoredFolder(Path.GetFileName(d))) stack.Push(d);
        }
    }

    /// <summary>Returns a path that does not exist yet by appending " 2", " 3", … to the file name.</summary>
    public static string UniquePath(string desiredPath)
    {
        if (!File.Exists(desiredPath) && !Directory.Exists(desiredPath)) return desiredPath;
        var dir = Path.GetDirectoryName(desiredPath)!;
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} {i}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    /// <summary>Strips characters that are invalid in file names on any of Windows, macOS or Linux.</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        var cleaned = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }

    /// <summary>A markdown-friendly relative link from a note to another file (forward slashes, spaces escaped).</summary>
    public static string RelativeLink(string fromNotePath, string targetPath)
    {
        var rel = Path.GetRelativePath(Path.GetDirectoryName(fromNotePath)!, targetPath).Replace('\\', '/');
        return rel.Replace(" ", "%20");
    }

    public static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                      Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                      OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    public static bool IsUnder(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
    }
}
