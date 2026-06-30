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
    List<Guid> SelectedElementIds { get; }
    SKColor BackgroundColor { get; }
    bool HasEvents { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }

    // Events
    event Action? CanvasInvalidated;
    event Action? SelectionInvalidated;
    event Action? UndoRedoStateChanged;
    event Action? BackgroundColorChanged;

    // Methods
    void SetBackgroundColor(SKColor color);
    void Undo();
    void Redo();
    void ClearSelection();
    List<CanvasElement> GetSelectedElements();
    void LoadCanvas(List<CanvasElement> elements);
    void ApplyEvent(Event @event, bool isLocalEvent = true);
    bool IsLocalSelection(Guid boundId);
}