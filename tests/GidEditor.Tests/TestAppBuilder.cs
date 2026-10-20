using Avalonia;
using Avalonia.Headless;
using GidEditor.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace GidEditor.Tests;

public class TestAppBuilder
{
    // Real Skia rendering (not headless drawing) so screenshot tests capture what users would see.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
