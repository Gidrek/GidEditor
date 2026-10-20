using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace GidEditor.Markdown;

/// <summary>Loads and caches images referenced from notes (local files relative to the note, or http/https URLs).</summary>
public static class ImageLoader
{
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, Bitmap Bitmap)> LocalCache = new();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> RemoteCache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static ImageLoader()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("GidEditor/1.0");
    }

    public static bool IsRemote(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves a markdown URL to a local file path, or null when it is remote or invalid.</summary>
    public static string? ResolveLocalPath(string url, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(url) || IsRemote(url)) return null;
        try
        {
            var path = url;
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return new Uri(path).LocalPath;
            var hash = path.IndexOfAny(['#', '?']);
            if (hash >= 0) path = path[..hash];
            path = Uri.UnescapeDataString(path);
            if (Path.IsPathRooted(path)) return path;
            return baseDirectory is null ? null : Path.GetFullPath(Path.Combine(baseDirectory, path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Bitmap? LoadLocal(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (LocalCache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Bitmap;
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            LocalCache[path] = (stamp, bitmap);
            return bitmap;
        }
        catch (Exception)
        {
            // Unsupported format (e.g. SVG) or a file still being written by a sync client.
            return null;
        }
    }

    public static Task<Bitmap?> LoadRemoteAsync(string url) =>
        RemoteCache.GetOrAdd(url, static async u =>
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(u).ConfigureAwait(false);
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch (Exception)
            {
                return null;
            }
        });
}
