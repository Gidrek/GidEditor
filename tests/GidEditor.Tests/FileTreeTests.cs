using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using GidEditor.Models;
using GidEditor.Services;
using GidEditor.ViewModels;
using GidEditor.Views;
using Xunit;

namespace GidEditor.Tests;

public class FileTreeTests
{
    /// <summary>Answers prompts with queued names instead of showing dialogs.</summary>
    private sealed class FakeUi : IUiServices
    {
        public Queue<string> Answers { get; } = new();
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickImagesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PromptAsync(string title, string message, string initialValue, string okText = "OK") =>
            Task.FromResult<string?>(Answers.Dequeue());
        public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> buttons, int destructiveIndex = -1) =>
            Task.FromResult(0);
        public void OpenExternal(string pathOrUrl) { }
        public void RevealInFileManager(string path) { }
    }

    private static (MainWindow Window, MainViewModel Vm, FakeUi Ui, string Root) Setup()
    {
        var root = Path.Combine(Path.GetTempPath(), "gideditor-tests", Guid.NewGuid().ToString("N"), "Notes");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Note.md"), "# Note\n");

        var settings = Path.Combine(Path.GetDirectoryName(root)!, "settings.json");
        var vm = new MainViewModel(new SettingsService(settings)) { Theme = ThemeMode.Light };
        var window = new MainWindow(vm) { Width = 1100, Height = 700 };
        window.Show();
        var ui = new FakeUi();
        vm.Attach(ui);
        vm.OpenWorkspace(root, restoreTabs: false);
        Pump();
        return (window, vm, ui, root);
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    [AvaloniaFact]
    public async Task Clicking_empty_tree_space_deselects_so_new_folder_goes_to_root()
    {
        var (window, vm, ui, root) = Setup();

        ui.Answers.Enqueue("Folder A");
        await vm.NewFolderCommand.ExecuteAsync(null);
        Pump();
        Assert.Equal(Path.Combine(root, "Folder A"), vm.SelectedNode?.FullPath);

        // Click the empty area under the last item.
        var tree = window.FindControl<TreeView>("FileTree")!;
        var point = tree.TranslatePoint(new Point(tree.Bounds.Width / 2, tree.Bounds.Height - 20), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump();
        Assert.Null(vm.SelectedNode);

        ui.Answers.Enqueue("Folder B");
        await vm.NewFolderCommand.ExecuteAsync(null);
        Assert.True(Directory.Exists(Path.Combine(root, "Folder B")));
        Assert.False(Directory.Exists(Path.Combine(root, "Folder A", "Folder B")));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Root_targeted_commands_ignore_the_selected_folder()
    {
        var (window, vm, ui, root) = Setup();
        ui.Answers.Enqueue("Folder A");
        await vm.NewFolderCommand.ExecuteAsync(null);
        Assert.NotNull(vm.SelectedNode);

        // What the header menu and the empty-space context menu pass.
        ui.Answers.Enqueue("At Root");
        await vm.NewNoteCommand.ExecuteAsync(vm.Root);
        Assert.True(File.Exists(Path.Combine(root, "At Root.md")));

        ui.Answers.Enqueue("Also Root");
        await vm.NewFolderCommand.ExecuteAsync(vm.Root);
        Assert.True(Directory.Exists(Path.Combine(root, "Also Root")));
        window.Close();
    }

    [AvaloniaFact]
    public async Task With_folder_selected_new_folder_goes_inside_it()
    {
        var (window, vm, ui, root) = Setup();
        ui.Answers.Enqueue("Parent");
        await vm.NewFolderCommand.ExecuteAsync(null);

        ui.Answers.Enqueue("Child");
        await vm.NewFolderCommand.ExecuteAsync(null);
        Assert.True(Directory.Exists(Path.Combine(root, "Parent", "Child")));
        window.Close();
    }
}
