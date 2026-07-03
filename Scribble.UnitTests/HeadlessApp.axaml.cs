using Avalonia;
using Avalonia.Markup.Xaml;

namespace Scribble.UnitTests;

public class HeadlessApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}