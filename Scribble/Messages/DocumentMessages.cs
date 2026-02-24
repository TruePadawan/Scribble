using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Scribble.Shared.Lib;

namespace Scribble.Messages;

// Holds the canvas data saved to a file
public record CanvasDataPayload(List<Stroke> Strokes, Color BackgroundColor);

// From Document View Model to MainViewModel asking for the current canvas data
public class RequestCanvasDataMessage : RequestMessage<CanvasDataPayload>
{
}

// Message from Document View Model to MainViewModel asking to load the canvas data
public record LoadCanvasDataMessage(List<Stroke> Strokes, string? BackgroundColorHex);

// Message from Document View Model to MainViewModel asking to clear the canvas
public record ClearCanvasMessage();