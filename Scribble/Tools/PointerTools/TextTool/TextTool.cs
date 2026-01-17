using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.TextTool;

public class TextTool : PointerToolsBase
{
    private readonly Canvas _canvasContainer;
    private TextBox? _currentTextBox;
    private readonly SKPaint _strokePaint;

    public TextTool(string name, MainViewModel viewModel, Canvas canvasContainer) : base(name, viewModel,
        LoadToolBitmap(typeof(TextTool), "text.png"))
    {
        _canvasContainer = canvasContainer;
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        _strokePaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 15,
            IsAntialias = true
        };
    }

    public override void HandlePointerClick(Point coord)
    {
        if (_currentTextBox != null)
        {
            FinalizeText();
            return;
        }

        _currentTextBox = new TextBox
        {
            MinWidth = 100,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.BlueViolet,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            AcceptsReturn = true
        };

        // Prevent the Scroll Viewer from jumping when the textbox gets focus
        _currentTextBox.AddHandler(Control.RequestBringIntoViewEvent, (sender, args) => { args.Handled = true; });

        Canvas.SetLeft(_currentTextBox, coord.X);
        Canvas.SetTop(_currentTextBox, coord.Y);

        // Intercept the key before the TextBox consumes it and adds a newline
        _currentTextBox.AddHandler(InputElement.KeyDownEvent, (sender, args) =>
        {
            // Commit when the only Enter key is pressed, don't accept Shift + Enter (used for new line)
            if (args.Key == Key.Enter && (args.KeyModifiers & KeyModifiers.Shift) == 0)
            {
                FinalizeText();
                // Mark as handled to prevent the newline character
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        _currentTextBox.LostFocus += (sender, args) => FinalizeText();
        _canvasContainer.Children.Add(_currentTextBox);
        _currentTextBox.Focus();
    }

    private void FinalizeText()
    {
        if (_currentTextBox == null) return;

        var text = _currentTextBox.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var textboxPos = new SKPoint((float)Canvas.GetLeft(_currentTextBox), (float)Canvas.GetTop(_currentTextBox));
            textboxPos.Y += _strokePaint.TextSize;
            var strokeId = Guid.NewGuid();
            // ViewModel.ApplyStrokeEvent(
            //     new StartStrokeEvent(strokeId, textboxPos, _strokePaint.Clone(), StrokeTool.Text));
            ViewModel.ApplyStrokeEvent(new AddTextEvent(strokeId, textboxPos, text, _strokePaint.Clone()));
            // ViewModel.ApplyStrokeEvent(new EndStrokeEvent(strokeId));
        }

        _canvasContainer.Children.Remove(_currentTextBox);
        _currentTextBox = null;
    }

    public override bool RenderOptions(Panel parent)
    {
        // Render a slider for controlling the font size and a color picker for text color
        var slider = new Slider
        {
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Minimum = 10,
            Maximum = 40,
            Value = _strokePaint.TextSize
        };
        slider.ValueChanged += ((sender, args) => { _strokePaint.TextSize = (float)args.NewValue; });
        slider.Padding = new Thickness(8, 0);

        var colorPicker = new ColorPicker
        {
            Color = Utilities.FromSkColor(_strokePaint.Color),
            IsColorSpectrumSliderVisible = false,
            Width = 164
        };
        colorPicker.ColorChanged += (sender, args) =>
        {
            var newColor = args.NewColor;
            _strokePaint.Color = Utilities.ToSkColor(newColor);
        };

        parent.Children.Add(CreateOptionControl(colorPicker, "Color"));
        parent.Children.Add(CreateOptionControl(slider, "Font Size"));
        parent.Width = 180;
        return true;
    }
}