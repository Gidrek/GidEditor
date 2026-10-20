using System;
using System.IO;
using Avalonia.Headless.XUnit;
using GidEditor.ViewModels;
using Xunit;

namespace GidEditor.Tests;

public class DocumentViewModelTests
{
    private static string TempNote(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "gideditor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "note.md");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Simulates a sync client (Dropbox, Drive) replacing the file.</summary>
    private static void WriteExternally(string path, string content)
    {
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
    }

    [AvaloniaFact]
    public void Edits_mark_dirty_and_save_writes_file()
    {
        var path = TempNote("hello");
        var doc = DocumentViewModel.Load(path, () => false);
        Assert.False(doc.IsDirty);

        doc.Document.Insert(5, " world");
        Assert.True(doc.IsDirty);
        Assert.True(doc.Save());
        Assert.False(doc.IsDirty);
        Assert.Equal("hello world", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void External_change_reloads_clean_document_in_place()
    {
        var path = TempNote("line one\nline two\n");
        var doc = DocumentViewModel.Load(path, () => false);

        WriteExternally(path, "line one\nline 2 from phone\n");
        doc.CheckDisk();

        Assert.Equal("line one\nline 2 from phone\n", doc.Document.Text);
        Assert.False(doc.IsDirty);
        Assert.False(doc.HasConflict);
        // The reload is a normal edit, so it can be undone.
        Assert.True(doc.Document.UndoStack.CanUndo);
    }

    [AvaloniaFact]
    public void External_change_while_dirty_raises_conflict_and_keeps_local_text()
    {
        var path = TempNote("base");
        var doc = DocumentViewModel.Load(path, () => false);
        doc.Document.Insert(4, " local");

        WriteExternally(path, "base remote");
        doc.CheckDisk();

        Assert.True(doc.HasConflict);
        Assert.Equal("base local", doc.Document.Text);

        doc.KeepMineCommand.Execute(null);
        Assert.False(doc.HasConflict);
        Assert.Equal("base local", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void Conflict_can_take_disk_version()
    {
        var path = TempNote("base");
        var doc = DocumentViewModel.Load(path, () => false);
        doc.Document.Insert(4, " local");
        WriteExternally(path, "base remote");
        doc.CheckDisk();

        doc.TakeDiskVersionCommand.Execute(null);
        Assert.Equal("base remote", doc.Document.Text);
        Assert.False(doc.IsDirty);
    }

    [AvaloniaFact]
    public void Own_save_is_not_reported_as_external_change()
    {
        var path = TempNote("a");
        var doc = DocumentViewModel.Load(path, () => false);
        doc.Document.Insert(1, "b");
        doc.Save();
        doc.CheckDisk();
        Assert.False(doc.HasConflict);
        Assert.Equal("ab", doc.Document.Text);
    }

    [AvaloniaFact]
    public void Deleted_file_is_flagged_missing_and_can_be_restored()
    {
        var path = TempNote("keep me");
        var doc = DocumentViewModel.Load(path, () => false);
        File.Delete(path);
        doc.CheckDisk();
        Assert.True(doc.IsMissing);

        doc.RestoreCommand.Execute(null);
        Assert.False(doc.IsMissing);
        Assert.Equal("keep me", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void Utf8_bom_is_preserved_on_save()
    {
        var path = TempNote("");
        File.WriteAllText(path, "bom", new System.Text.UTF8Encoding(true));
        var doc = DocumentViewModel.Load(path, () => false);
        doc.Document.Insert(3, "!");
        doc.Save();
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }
}
