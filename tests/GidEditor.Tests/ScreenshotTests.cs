using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using GidEditor.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GidEditor.Models;
using GidEditor.Services;
using GidEditor.ViewModels;
using GidEditor.Views;
using Xunit;

namespace GidEditor.Tests;

/// <summary>
/// Renders the real main window off-screen and saves PNGs to bin/.../snapshots for visual review.
/// They also act as smoke tests: any exception while building or rendering the UI fails them.
/// </summary>
public class ScreenshotTests
{
    private static readonly string SnapshotDir = Path.Combine(AppContext.BaseDirectory, "snapshots");

    private static string CopySampleWorkspace()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "SampleNotes");
        var target = Path.Combine(Path.GetTempPath(), "gideditor-tests", Guid.NewGuid().ToString("N"), "My Notes");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
        return target;
    }

    private static (MainWindow Window, MainViewModel Vm) CreateWindow(ThemeMode theme)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), "gideditor-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var vm = new MainViewModel(new SettingsService(settingsPath)) { Theme = theme };
        var window = new MainWindow(vm) { Width = 1360, Height = 860 };
        window.Show();
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        for (var i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void Save(MainWindow window, string name)
    {
        Pump();
        Directory.CreateDirectory(SnapshotDir);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(SnapshotDir, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, ViewMode.Split)]
    [InlineData(ThemeMode.Dark, ViewMode.Split)]
    [InlineData(ThemeMode.Light, ViewMode.Editor)]
    [InlineData(ThemeMode.Dark, ViewMode.Preview)]
    public void Workspace_with_open_note(ThemeMode theme, ViewMode mode)
    {
        var (window, vm) = CreateWindow(theme);
        var workspace = CopySampleWorkspace();
        vm.OpenWorkspace(workspace, restoreTabs: false);
        vm.OpenFile(Path.Combine(workspace, "Ideas.md"));
        vm.OpenFile(Path.Combine(workspace, "Welcome.md"));
        vm.ViewMode = mode;
        Save(window, $"workspace-{theme}-{mode}".ToLowerInvariant());

        if (mode == ViewMode.Preview)
        {
            window.FindControl<MarkdownViewer>("Preview")!.ScrollViewer.Offset = new Vector(0, 760);
            Save(window, $"workspace-{theme}-{mode}-scrolled".ToLowerInvariant());
        }

        Assert.Equal(2, vm.Documents.Count);
        Assert.Equal("Welcome", vm.ActiveDocument?.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void Welcome_screen()
    {
        var (window, _) = CreateWindow(ThemeMode.Light);
        Save(window, "welcome-light");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Search_panel_finds_notes()
    {
        var (window, vm) = CreateWindow(ThemeMode.Light);
        var workspace = CopySampleWorkspace();
        vm.OpenWorkspace(workspace, restoreTabs: false);
        vm.ShowSearchCommand.Execute(null);
        vm.SearchQuery = "roadmap";
        await vm.SearchNowAsync();

        // Name matches first, then by number of hits (Welcome mentions "roadmap" twice).
        Assert.Equal(["Roadmap", "Welcome", "Ideas"], vm.SearchResults.Select(r => r.FileName));
        Save(window, "search-light");
        window.Close();
    }
}
