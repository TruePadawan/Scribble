using Avalonia;
using Avalonia.Headless;
using Scribble.UnitTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Scribble.UnitTests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            // Decode bitmaps at their actual size for export and clipboard tests.
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
