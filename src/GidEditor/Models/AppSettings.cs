using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GidEditor.Models;

public enum ViewMode
{
    Editor,
    Split,
    Preview,
}

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public sealed class AppSettings
{
    public string? WorkspacePath { get; set; }
    public List<string> RecentWorkspaces { get; set; } = [];

    /// <summary>Open tabs, stored as absolute paths.</summary>
    public List<string> OpenFiles { get; set; } = [];
    public string? ActiveFile { get; set; }

    public ViewMode ViewMode { get; set; } = ViewMode.Split;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool Autosave { get; set; } = true;
    public bool MonospaceEditor { get; set; }
    public bool SidebarVisible { get; set; } = true;
    public double SidebarWidth { get; set; } = 260;
    public double EditorFontSize { get; set; } = 15;

    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
