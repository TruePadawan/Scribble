using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Scribble.Tests.TestAppBuilder))]

namespace Scribble.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}