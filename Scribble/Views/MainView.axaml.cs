using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace Scribble.Views;

public partial class MainView : UserControl
{
    private bool _mouseIsPressed = false;

    public MainView()
    {
        InitializeComponent();
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _mouseIsPressed = true;
    }

    private void InputElement_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseIsPressed = false;
    }

    private void InputElement_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_mouseIsPressed) return;
        var mousePosition = e.GetPosition(MainCanvas);
        var pixel = new Rectangle
        {
            Width = 5,
            Height = 5,
            Fill = Brushes.Red
        };
        Canvas.SetLeft(pixel, mousePosition.X);
        Canvas.SetTop(pixel, mousePosition.Y);
        MainCanvas.Children.Add(pixel);
    }
}