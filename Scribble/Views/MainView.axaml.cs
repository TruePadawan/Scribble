using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace Scribble.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void InputElement_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed)
        {
            Draw(e.GetPosition(MainCanvas));
        };
        
    }

    private void Draw(Point inputDevicePosition)
    {
        // Draw pixels on the canvas
        var pixel = new Rectangle
        {
            Width = 2,
            Height = 2,
            Fill = Brushes.Red
        };
        Canvas.SetLeft(pixel, inputDevicePosition.X);
        Canvas.SetTop(pixel, inputDevicePosition.Y);
        MainCanvas.Children.Add(pixel);
    }
}