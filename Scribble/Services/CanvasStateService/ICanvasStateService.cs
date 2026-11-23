using System;
using System.Collections.Generic;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService;

public interface ICanvasStateService
{
    // State
    IReadOnlyList<CanvasElement> CanvasElements { get; }
    Queue<Event> CanvasEvents { get; }
    Guid? ActiveSelectionBoundId { get; }
    IReadOnlyList<Guid> SelectedElementIds { get; }
    SKColor BackgroundColor { get; }
    bool IsRoomActive { get; }
    bool HasEvents { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }

    // Events
    event Action? CanvasInvalidated;

    // Signals a change to the content written to a document.
    event Action? ContentChanged;
    event Action<IReadOnlyList<Guid>, bool>? SelectionInvalidated;
    event Action? UndoRedoStateChanged;
    event Action? BackgroundColorChanged;

    // Methods
    void SetBackgroundColor(SKColor color);
    void Undo();
    void Redo();
    void ClearSelection();

    /// <summary>Returns selectable elements enclosed by a world-space marquee.</summary>
    List<Guid> GetEnclosedElementIds(SKRect bounds);
    List<CanvasElement> GetSelectedElements();
    void LoadCanvas(List<CanvasElement> elements);
    void ApplyEvent(Event @event, bool isLocalEvent = true);
}