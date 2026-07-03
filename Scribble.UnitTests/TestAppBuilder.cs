using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Scribble.UnitTests.TestAppBuilder))]

namespace Scribble.UnitTests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}