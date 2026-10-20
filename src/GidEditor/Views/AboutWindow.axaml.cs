using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using GidEditor.Services;

namespace GidEditor.Views;

/// <summary>App version, GidEditor's own license, and credits for every bundled open source component.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        VersionText = $"Version {LicenseNotices.AppVersion}";
        AppLicense = LicenseNotices.Reflow(LicenseNotices.AppLicense());
        Components = LicenseNotices.ThirdParty();
        InitializeComponent();
        DataContext = this;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    public string VersionText { get; }

    public string AppLicense { get; }

    public IReadOnlyList<OpenSourceComponent> Components { get; }
}
