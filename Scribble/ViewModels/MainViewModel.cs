using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Lib;
using Scribble.Lib.BitmapRenderer;

namespace Scribble.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly Vector _dpi = new(96, 96);
    private const int CanvasWidth = 10000;
    private const int CanvasHeight = 10000;
    public const int CanvasSnapshotInterval = 10;

    public Color BackgroundColor { get; }
    private WriteableBitmap? _snapshotBitmap;
    public int CheckpointIndex { get; private set; } = -1;

    public WriteableBitmap WhiteboardBitmap { get; }
    public ScaleTransform ScaleTransform { get; }

    public readonly BitmapEventsManager BitmapEventsManager;
    public readonly BitmapRendererBase BitmapRenderer;


    public MainViewModel(BitmapRendererBase bitmapRenderer)
    {
        BackgroundColor = Colors.Black;
        ScaleTransform = new ScaleTransform(1, 1);
        BitmapEventsManager = new BitmapEventsManager(this);
        BitmapRenderer = bitmapRenderer;

        // Initialize the bitmap with a large dimension
        WhiteboardBitmap = new WriteableBitmap(new PixelSize(CanvasWidth, CanvasHeight), _dpi, PixelFormat.Bgra8888);

        using var frameBuffer = WhiteboardBitmap.Lock();
        BitmapRenderer.ClearBitmap(frameBuffer, BackgroundColor);
    }

    public Vector GetCanvasDimensions() => new Vector(CanvasWidth, CanvasHeight);

    public double GetCurrentScale() => ScaleTransform.ScaleX;

    public void SetCurrentScale(double newScale)
    {
        ScaleTransform.ScaleX = newScale;
        ScaleTransform.ScaleY = newScale;
    }

    // // TODO: Should keep track of anti-aliased pixels and update them
    // public unsafe void ChangeBackgroundColor(Color color)
    // {
    //     if (color == BackgroundColor) return;
    //
    //     using var frame = WhiteboardBitmap.Lock();
    //     var address = frame.Address;
    //     int stride = frame.RowBytes;
    //     var bitmapPtr = (byte*)address.ToPointer();
    //
    //     int width = WhiteboardBitmap.PixelSize.Width;
    //     int height = WhiteboardBitmap.PixelSize.Height;
    //     for (int y = 0; y < height; ++y)
    //     {
    //         for (int x = 0; x < width; ++x)
    //         {
    //             long offset = (long)y * stride + (long)x * BytesPerPixel;
    //             // Update all pixels that are the color of the previous background color to the new one
    //             var b = bitmapPtr[offset];
    //             var g = bitmapPtr[offset + 1];
    //             var r = bitmapPtr[offset + 2];
    //             var a = bitmapPtr[offset + 3];
    //             if (b != BackgroundColor.B || g != BackgroundColor.G || r != BackgroundColor.R ||
    //                 a != BackgroundColor.A) continue;
    //             bitmapPtr[offset] = color.B;
    //             bitmapPtr[offset + 1] = color.G;
    //             bitmapPtr[offset + 2] = color.R;
    //             bitmapPtr[offset + 3] = color.A;
    //         }
    //     }
    // }

    public void UndoLastOperation()
    {
        if (BitmapEventsManager.Events.Count == 0 || BitmapEventsManager.CurrentEventIndex == -1) return;

        using var frameBuffer = WhiteboardBitmap.Lock();
        // Fast Undo: Use checkpoint if the target state is after our saved checkpoint
        if (CheckpointIndex != -1 && BitmapEventsManager.CurrentEventIndex - 1 >= CheckpointIndex)
        {
            using var checkpointBuffer = _snapshotBitmap!.Lock();
            var size = WhiteboardBitmap.PixelSize;
            var srcSize = checkpointBuffer.RowBytes * size.Height;
            var dstSize = frameBuffer.RowBytes * size.Height;

            unsafe
            {
                Buffer.MemoryCopy(checkpointBuffer.Address.ToPointer(), frameBuffer.Address.ToPointer(), dstSize,
                    srcSize);
            }

            // Replay the remaining small events
            for (int i = CheckpointIndex + 1; i <= BitmapEventsManager.CurrentEventIndex - 1; i++)
            {
                BitmapEventsManager.ApplyEvent(BitmapEventsManager.Events[i], frameBuffer);
            }
        }
        else
        {
            // Full Replay
            BitmapRenderer.ClearBitmap(frameBuffer, BackgroundColor);
            for (var i = 0; i < BitmapEventsManager.CurrentEventIndex; i++)
            {
                BitmapEventsManager.ApplyEvent(BitmapEventsManager.Events[i], frameBuffer);
            }
        }

        BitmapEventsManager.CurrentEventIndex -= 1;
    }

    public void RedoLastOperation()
    {
        var eventsCount = BitmapEventsManager.Events.Count;
        if (eventsCount == 0 || BitmapEventsManager.CurrentEventIndex >= eventsCount - 1) return;

        using var frameBuffer = WhiteboardBitmap.Lock();

        BitmapEventsManager.CurrentEventIndex += 1;
        var @eventToApply = BitmapEventsManager.Events[BitmapEventsManager.CurrentEventIndex];
        BitmapEventsManager.ApplyEvent(@eventToApply, frameBuffer);
    }

    public void UpdateCanvasSnapshot()
    {
        var size = WhiteboardBitmap.PixelSize;
        var dpi = WhiteboardBitmap.Dpi;

        // Lazy initialization
        if (_snapshotBitmap == null || _snapshotBitmap.PixelSize != size)
        {
            _snapshotBitmap?.Dispose();
            // Create the backup bitmap
            _snapshotBitmap = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888);
        }

        // copy screen to checkpoint
        using var srcBuffer = WhiteboardBitmap.Lock();
        using var checkpointBuffer = _snapshotBitmap.Lock();
        var srcSize = srcBuffer.RowBytes * size.Height;
        var dstSize = checkpointBuffer.RowBytes * size.Height;

        unsafe
        {
            Buffer.MemoryCopy(srcBuffer.Address.ToPointer(), checkpointBuffer.Address.ToPointer(), dstSize, srcSize);
        }

        CheckpointIndex = BitmapEventsManager.CurrentEventIndex;
    }

    public void InvalidateCanvasSnapshot()
    {
        CheckpointIndex = -1;
    }
}