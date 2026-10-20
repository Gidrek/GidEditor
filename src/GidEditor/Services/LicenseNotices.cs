using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Platform;

namespace GidEditor.Services;

public sealed record OpenSourceComponent(string Name, string Description, string Url, string License, string Text)
{
    public Uri? Uri => System.Uri.TryCreate(Url, UriKind.Absolute, out var u) ? u : null;

    public string DisplayText => LicenseNotices.Reflow(Text);
}

/// <summary>
/// Reads the app's own license and THIRD-PARTY-NOTICES.txt (embedded as resources) for the About screen.
/// The notices file is the single source of truth; it is also shipped next to the executable.
/// </summary>
public static class LicenseNotices
{
    private const string Separator = "================================================================================";
    private const string BodySeparator = "--------------------------------------------------------------------------------";

    public static string AppVersion
    {
        get
        {
            var version = typeof(LicenseNotices).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            // Drop the "+<commit>" build metadata the SDK appends.
            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }

    public static string AppLicense() => ReadResource("LICENSE");

    public static IReadOnlyList<OpenSourceComponent> ThirdParty() => Parse(ReadResource("THIRD-PARTY-NOTICES.txt"));

    /// <summary>
    /// Each entry is: separator line, name, description, URL, SPDX license id, body separator, license text.
    /// Anything before the first separator is an introduction and is skipped.
    /// </summary>
    public static IReadOnlyList<OpenSourceComponent> Parse(string notices)
    {
        var result = new List<OpenSourceComponent>();
        var blocks = notices.Replace("\r\n", "\n").Split(Separator + "\n");
        for (var i = 1; i < blocks.Length; i++)
        {
            var split = blocks[i].Split(BodySeparator + "\n", 2);
            if (split.Length != 2) continue;
            var header = split[0].TrimEnd('\n').Split('\n');
            if (header.Length < 4) continue;
            result.Add(new OpenSourceComponent(header[0], header[1], header[2], header[3], split[1].Trim('\n')));
        }
        return result;
    }

    /// <summary>
    /// Joins the hard-wrapped lines of plain prose paragraphs so license texts wrap to the window width.
    /// Paragraphs that look formatted (indented, lists, comment prefixes) are left exactly as written.
    /// </summary>
    public static string Reflow(string text)
    {
        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n");
        for (var i = 0; i < paragraphs.Length; i++)
        {
            var lines = paragraphs[i].Split('\n');
            if (lines.Length > 1 && lines.All(IsProseLine))
                paragraphs[i] = string.Join(' ', lines.Select(l => l.Trim()));
        }
        return string.Join("\n\n", paragraphs);
    }

    private static bool IsProseLine(string line) =>
        line.Length > 0 && (char.IsLetter(line[0]) || line[0] is '(' or '"' or '\'') && !line.StartsWith("Copyright", StringComparison.Ordinal);

    private static string ReadResource(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://GidEditor/Assets/Legal/{name}"));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
