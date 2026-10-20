using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GidEditor.Services;
using GidEditor.Views;
using Xunit;

namespace GidEditor.Tests;

public class LicenseNoticesTests
{
    /// <summary>Which notices entry covers an assembly, by name prefix. More specific prefixes first.</summary>
    private static readonly (string Prefix, string Component)[] Coverage =
    [
        ("AvaloniaEdit", "AvaloniaEdit"),
        ("Avalonia", "Avalonia"),
        ("Markdig", "Markdig"),
        ("CommunityToolkit.Mvvm", "CommunityToolkit.Mvvm"),
        ("FluentIcons", "FluentIcons"),
        ("SkiaSharp", "SkiaSharp and HarfBuzzSharp"),
        ("HarfBuzzSharp", "SkiaSharp and HarfBuzzSharp"),
        ("MicroCom", "MicroCom"),
        ("Tmds.DBus", "Tmds.DBus"),
    ];

    /// <summary>Framework assemblies (covered by the .NET Runtime entry) and Debug-only tooling.</summary>
    private static bool IsExempt(string name) =>
        name is "GidEditor" or "netstandard" or "mscorlib" or "WindowsBase" ||
        name.StartsWith("System", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
        name.StartsWith("AvaloniaUI.DiagnosticsSupport", StringComparison.Ordinal);

    [AvaloniaFact]
    public void Notices_parse_into_complete_entries()
    {
        var components = LicenseNotices.ThirdParty();
        Assert.True(components.Count >= 14);
        foreach (var c in components)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.NotNull(c.Uri);
            Assert.False(string.IsNullOrWhiteSpace(c.License));
            Assert.Contains("copyright", c.Text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("MIT License", LicenseNotices.AppLicense());
    }

    [Fact]
    public void Reflow_joins_prose_but_keeps_copyright_lines_and_formatted_blocks()
    {
        var text = "Copyright (c) 2020 A\nCopyright (c) 2021 B\n\nPermission is hereby\ngranted, free.\n\n * item one\n * item two";
        Assert.Equal("Copyright (c) 2020 A\nCopyright (c) 2021 B\n\nPermission is hereby granted, free.\n\n * item one\n * item two",
            LicenseNotices.Reflow(text));
    }

    [AvaloniaFact]
    public void Every_shipped_third_party_assembly_is_credited()
    {
        var credited = LicenseNotices.ThirdParty().Select(c => c.Name).ToHashSet();
        var missing = new List<string>();
        var shipped = ReferencedAssemblies(typeof(App).Assembly).ToList();

        // Guard against the walk silently finding nothing: these are only reachable transitively.
        Assert.Contains("SkiaSharp", shipped);
        Assert.Contains("HarfBuzzSharp", shipped);

        foreach (var name in shipped)
        {
            if (IsExempt(name)) continue;
            var entry = Coverage.FirstOrDefault(c => name.StartsWith(c.Prefix, StringComparison.Ordinal)).Component;
            if (entry is null || !credited.Contains(entry)) missing.Add(name);
        }

        Assert.True(missing.Count == 0,
            "Add these to THIRD-PARTY-NOTICES.txt (and to the coverage table in this test): " + string.Join(", ", missing));
    }

    private static IEnumerable<string> ReferencedAssemblies(Assembly root)
    {
        var seen = new HashSet<string> { root.GetName().Name! };
        var queue = new Queue<Assembly>([root]);
        while (queue.Count > 0)
        {
            foreach (var reference in queue.Dequeue().GetReferencedAssemblies())
            {
                if (!seen.Add(reference.Name!) || IsExempt(reference.Name!)) continue;
                yield return reference.Name!;
                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch (FileNotFoundException)
                {
                    // Platform-specific assembly not present in this build; its name was still checked.
                }
            }
        }
    }

    [AvaloniaFact]
    public void About_window_renders_credits()
    {
        var window = new AboutWindow();
        window.Show();
        var expanders = window.GetVisualDescendants().OfType<Expander>().ToList();
        Assert.True(expanders.Count >= 15);
        expanders[2].IsExpanded = true;
        for (var i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        var dir = Path.Combine(AppContext.BaseDirectory, "snapshots");
        Directory.CreateDirectory(dir);
        window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "about-light.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close();
    }
}
