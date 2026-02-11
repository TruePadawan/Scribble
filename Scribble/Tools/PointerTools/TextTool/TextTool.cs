using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Shared.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.TextTool;

public class TextTool : StrokeTool
{
    private readonly Canvas _canvasContainer;
    private TextBox? _currentTextBox;
    private Guid _actionId = Guid.NewGuid();

    public TextTool(string name, MainViewModel viewModel, Canvas canvasContainer) : base(name, viewModel,
        LoadToolBitmap(typeof(TextTool), "text.png"))
    {
        ToolOptions = [ToolOption.StrokeColor, ToolOption.FontSize];
        _canvasContainer = canvasContainer;
        _canvasContainer.Focusable = true;
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        StrokePaint = new StrokePaint
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
            FinalizeText(true);
            return;
        }

        _actionId = Guid.NewGuid();
        _currentTextBox = new TextBox
        {
            MinWidth = 100,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Utilities.FromSkColor(StrokePaint.Color)),
            FontSize = StrokePaint.TextSize,
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
                _currentTextBox.LostFocus -= TextboxLostFocusHandler;
                FinalizeText(true);
                // Mark as handled to prevent the newline character
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        _currentTextBox.LostFocus += TextboxLostFocusHandler;
        _canvasContainer.Children.Add(_currentTextBox);
        _currentTextBox.Focus();
    }

    private void TextboxLostFocusHandler(object? sender, RoutedEventArgs args)
    {
        FinalizeText(false);
    }

    private void FinalizeText(bool restoreFocus)
    {
        if (_currentTextBox == null) return;

        var text = _currentTextBox.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var textboxPos = new SKPoint((float)Canvas.GetLeft(_currentTextBox), (float)Canvas.GetTop(_currentTextBox));
            textboxPos.Y += StrokePaint.TextSize;
            var strokeId = Guid.NewGuid();
            ViewModel.ApplyEvent(new AddTextEvent(_actionId, strokeId, textboxPos, text, StrokePaint.Clone()));
        }

        _canvasContainer.Children.Remove(_currentTextBox);
        _currentTextBox = null;
        if (restoreFocus)
        {
            _canvasContainer.Focus();
        }
    }
}