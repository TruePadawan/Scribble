using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;

namespace Scribble.Controls;

/// <summary>Displays selection handles and forwards their pointer gestures to the editor.</summary>
public partial class SelectionOverlay : UserControl
{
    private const float RotationHandleOffset = 21;

    public Border SelectionBorder { get; }
    public Border SelectionRotationButton { get; }

    public event EventHandler<PointerPressedEventArgs>? MovePressed;
    public event EventHandler<PointerEventArgs>? MoveMoved;
    public event EventHandler<PointerReleasedEventArgs>? MoveReleased;
    public event EventHandler<PointerPressedEventArgs>? RotatePressed;
    public event EventHandler<PointerEventArgs>? RotateMoved;
    public event EventHandler<PointerReleasedEventArgs>? RotateReleased;
    public event EventHandler<PointerPressedEventArgs>? ScalePressed;
    public event EventHandler<PointerEventArgs>? ScaleMoved;
    public event EventHandler<PointerReleasedEventArgs>? ScaleReleased;
    public event EventHandler? GestureCaptureLost;

    /// <summary>Wires the selection handles to gesture capture and editor callbacks.</summary>
    public SelectionOverlay()
    {
        InitializeComponent();
        SelectionBorder = this.FindControl<Border>("SelectionBorderControl")!;
        SelectionRotationButton = this.FindControl<Border>("SelectionRotationBtn")!;

        // Keep pointer capture on the active handle so drags continue outside its bounds.
        AttachGesture(SelectionBorder, (sender, args) => MovePressed?.Invoke(sender, args),
            (sender, args) => MoveMoved?.Invoke(sender, args),
            (sender, args) => MoveReleased?.Invoke(sender, args),
            () => GestureCaptureLost?.Invoke(SelectionBorder, EventArgs.Empty));
        AttachGesture(SelectionRotationButton, (sender, args) => RotatePressed?.Invoke(sender, args),
            (sender, args) => RotateMoved?.Invoke(sender, args),
            (sender, args) => RotateReleased?.Invoke(sender, args),
            () => GestureCaptureLost?.Invoke(SelectionRotationButton, EventArgs.Empty));
        foreach (var name in new[] { "ScaleHandleTl", "ScaleHandleTr", "ScaleHandleBl", "ScaleHandleBr" })
        {
            var handle = this.FindControl<Border>(name)!;
            AttachGesture(handle, (sender, args) => ScalePressed?.Invoke(sender, args),
                (sender, args) => ScaleMoved?.Invoke(sender, args),
                (sender, args) => ScaleReleased?.Invoke(sender, args),
                () => GestureCaptureLost?.Invoke(handle, EventArgs.Empty));
        }
    }

    /// <summary>Updates the overlay from world-derived screen geometry.</summary>
    public void SetSelectionGeometry(SKRect screenBounds, float rotationDegrees, SKPoint rotationCenter, bool visible)
    {
        const float rotationHandleOffset = RotationHandleOffset;
        Canvas.SetLeft(this, screenBounds.Left);
        Canvas.SetTop(this, screenBounds.Top - rotationHandleOffset);
        Width = screenBounds.Width;
        Height = screenBounds.Height + rotationHandleOffset;
        this.FindControl<Grid>("SelectionBoxContainer")!.Width = screenBounds.Width;
        this.FindControl<Grid>("SelectionBoxContainer")!.Height = screenBounds.Height;

        if (Math.Abs(rotationDegrees) > 0.001f && screenBounds.Width > 0 && Height > 0)
        {
            var localCenterX = rotationCenter.X - screenBounds.Left;
            var localCenterY = rotationCenter.Y - (screenBounds.Top - rotationHandleOffset);
            RenderTransformOrigin = new RelativePoint(localCenterX / screenBounds.Width,
                localCenterY / Height, RelativeUnit.Relative);
            RenderTransform = new RotateTransform(rotationDegrees);
        }
        else
        {
            RenderTransform = null;
        }

        IsVisible = visible;
    }

    /// <summary>Hooks capture, movement forwarding, and release for one overlay handle.</summary>
    private static void AttachGesture(Control control,
        EventHandler<PointerPressedEventArgs> pressed,
        EventHandler<PointerEventArgs> moved,
        EventHandler<PointerReleasedEventArgs> released,
        Action captureLost)
    {
        control.PointerPressed += (sender, args) =>
        {
            if (args.GetCurrentPoint(control).Properties.IsLeftButtonPressed) args.Pointer.Capture(control);
            pressed(sender, args);
        };
        control.PointerMoved += moved;
        control.PointerReleased += (sender, args) =>
        {
            released(sender, args);
            args.Pointer.Capture(null);
        };
        control.PointerCaptureLost += (_, _) => captureLost();
    }
}
