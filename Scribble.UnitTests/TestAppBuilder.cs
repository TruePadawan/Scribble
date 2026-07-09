using Avalonia;
using Avalonia.Headless;
using Scribble.UnitTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Scribble.UnitTests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}