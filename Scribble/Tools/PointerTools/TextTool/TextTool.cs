using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Services;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.State;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.TextTool;

public class TextTool : StrokeTool
{
    private readonly Canvas _canvasContainer;
    private TextBox? _currentTextBox;
    private TextStroke? _editingStroke;
    private Guid _actionId = Guid.NewGuid();

    public TextTool(string name, CanvasStateService canvasState, Canvas canvasContainer) : base(name, canvasState,
        LoadToolBitmap(typeof(TextTool), "text.png"))
    {
        ToolOptions = [ToolOption.StrokeColor, ToolOption.FontSize, ToolOption.FontCasing, ToolOption.FontStyle];
        _canvasContainer = canvasContainer;
        _canvasContainer.Focusable = true;
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        StrokePaint = new StrokePaint
        {
            IsStroke = false,
        };

        HotKey = new KeyGesture(Key.D8);
        ToolTip = "Text Tool - 8";
    }

    public override void HandlePointerClick(SKPoint coord)
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

        // coord is in world-space, convert to screen-space for overlay positioning
        var screenPos = CameraState.WorldToScreen(coord);
        Canvas.SetLeft(_currentTextBox, screenPos.X);
        Canvas.SetTop(_currentTextBox, screenPos.Y);

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

    public void StartEditing(TextStroke textStroke)
    {
        if (_currentTextBox != null)
        {
            FinalizeText(true);
        }

        _editingStroke = textStroke;
        _actionId = Guid.NewGuid();
        _currentTextBox = new TextBox
        {
            Text = textStroke.Text,
            MinWidth = 100,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Utilities.FromSkColor(StrokePaint.Color)),
            FontSize = textStroke.Paint.TextSize,
            BorderThickness = new Thickness(1),
            AcceptsReturn = true
        };

        // Prevent the Scroll Viewer from jumping when the textbox gets focus
        _currentTextBox.AddHandler(Control.RequestBringIntoViewEvent, (sender, args) => { args.Handled = true; });

        var actualPosition = textStroke.TransformMatrix.MapPoint(textStroke.Position);
        // Convert world-space position to screen-space for overlay positioning
        var screenPos = CameraState.WorldToScreen(actualPosition);
        Canvas.SetLeft(_currentTextBox, screenPos.X);
        Canvas.SetTop(_currentTextBox, screenPos.Y - textStroke.Paint.TextSize * CameraState.Zoom);

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
        _currentTextBox.CaretIndex = _currentTextBox.Text?.Length ?? 0;
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
            if (_editingStroke != null && _editingStroke.Text != text)
            {
                CanvasState.ApplyEvent(new UpdateTextEvent(_actionId, _editingStroke.Id, text));
            }
            else if (_editingStroke == null)
            {
                // Convert screen-space TextBox position back to world-space for the event
                var screenPos = new SKPoint((float)Canvas.GetLeft(_currentTextBox),
                    (float)Canvas.GetTop(_currentTextBox));
                var textboxPos = CameraState.ScreenToWorld(screenPos);
                textboxPos.Y += StrokePaint.TextSize;
                var strokeId = Guid.NewGuid();
                CanvasState.ApplyEvent(new AddTextEvent(_actionId, strokeId, textboxPos, text, StrokePaint.Clone(),
                    ToolOptions));
            }
        }

        _canvasContainer.Children.Remove(_currentTextBox);
        _currentTextBox = null;
        _editingStroke = null;
        if (restoreFocus)
        {
            _canvasContainer.Focus();
        }
    }
}