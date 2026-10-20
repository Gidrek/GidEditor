using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace GidEditor.Services;

/// <summary>
/// Moves files and folders to the operating system's trash so deleted notes can be recovered.
/// Returns false when the platform trash is unavailable; callers then decide whether to delete permanently.
/// </summary>
public static class TrashService
{
    public static bool TryMoveToTrash(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return TrashWindows(path);
            if (OperatingSystem.IsMacOS()) return TrashMac(path);
            return TrashFreedesktop(path) || RunTool("gio", "trash", path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void DeletePermanently(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    private static bool TrashWindows(string path)
    {
        if (Directory.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        else
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        return !File.Exists(path) && !Directory.Exists(path);
    }

    private static bool TrashMac(string path)
    {
        var escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return RunTool("osascript", "-e", $"tell application \"Finder\" to delete POSIX file \"{escaped}\"");
    }

    /// <summary>Implements the freedesktop.org trash spec for the user's home trash (~/.local/share/Trash).</summary>
    private static bool TrashFreedesktop(string path)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        var trash = Path.Combine(dataHome, "Trash");
        var filesDir = Path.Combine(trash, "files");
        var infoDir = Path.Combine(trash, "info");
        Directory.CreateDirectory(filesDir);
        Directory.CreateDirectory(infoDir);

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        var target = Path.Combine(filesDir, name);
        for (var i = 2; File.Exists(target) || Directory.Exists(target) || File.Exists(Path.Combine(infoDir, Path.GetFileName(target) + ".trashinfo")); i++)
            target = Path.Combine(filesDir, $"{Path.GetFileNameWithoutExtension(name)}.{i}{Path.GetExtension(name)}");

        var infoPath = Path.Combine(infoDir, Path.GetFileName(target) + ".trashinfo");
        File.WriteAllText(infoPath,
            "[Trash Info]\n" +
            $"Path={Uri.EscapeDataString(Path.GetFullPath(path)).Replace("%2F", "/")}\n" +
            $"DeletionDate={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}\n");

        try
        {
            // Move fails across file systems (e.g. a USB drive); fall back to other strategies then.
            if (Directory.Exists(path)) Directory.Move(path, target);
            else File.Move(path, target);
            return true;
        }
        catch (IOException)
        {
            File.Delete(infoPath);
            return false;
        }
    }

    private static bool RunTool(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(10_000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
