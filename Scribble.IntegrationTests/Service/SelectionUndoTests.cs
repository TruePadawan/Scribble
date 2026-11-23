using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Scribble.Controls;
using Scribble.Extensions;
using Scribble.IntegrationTests.Hub;
using Scribble.Services.CanvasStateService;
using Scribble.Services.MultiUserDrawing;
using ISelectable = Scribble.Shared.Lib.CanvasElements.ISelectable;
using Scribble.Tools.PointerTools.SelectTool;
using Scribble.Tools.PointerTools.PencilTool;
using Scribble.ViewModels;
using Scribble.Views;
using SkiaSharp;

namespace Scribble.IntegrationTests.Service;

/// <summary>Covers selection undo through the editor tools and the visible overlay.</summary>
[Collection("HubTests")]
public class SelectionUndoTests(HubServerFixture fixture) : IClassFixture<HubServerFixture>
{
    /// <summary>Undoing a blank click should restore the previously selected stroke and its handles.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Undo_AfterBlankClickAndNewStroke_RestoresSelectionOverlay(bool inRoom)
    {
        var alice = new MultiUserDrawingService(fixture.CreateHubConnection, Dispatcher.UIThread);
        var bob = new MultiUserDrawingService(fixture.CreateHubConnection, Dispatcher.UIThread);
        var registrations = new ServiceCollection();
        registrations.AddCommonServices();
        registrations.AddSingleton<IMultiUserDrawingService>(alice);
        await using var services = registrations.BuildServiceProvider();
        var canvas = services.GetRequiredService<ICanvasStateService>();
        var editor = services.GetRequiredService<MainViewModel>();
        var view = new MainView(services) { DataContext = editor };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            if (inRoom)
            {
                var code = Guid.NewGuid().ToString();
                await alice.JoinRoomAsync(code, "Alice");
                await bob.JoinRoomAsync(code, "Bob");
                Assert.Equal(2, bob.Room!.Clients.Count);
            }

            var pencil = editor.UiStateViewModel.AvailableTools.OfType<PencilTool>().Single();
            var select = editor.UiStateViewModel.AvailableTools.OfType<SelectTool>().Single();
            var overlay = view.FindControl<SelectionOverlay>("SelectionOverlay")!;
            editor.UiStateViewModel.ActivePointerTool = pencil;
            DrawStroke(pencil, new SKPoint(20, 40), new SKPoint(35, 55));
            DrawStroke(pencil, new SKPoint(80, 40), new SKPoint(95, 55));

            editor.UiStateViewModel.ActivePointerTool = select;
            var ids = canvas.CanvasElements.Select(e => e.Id).ToArray();
            foreach (var element in canvas.CanvasElements.ToArray())
            {
                select.SelectElements([(ISelectable)element]);
                Assert.True(overlay.IsVisible);
            }

            Assert.Equal([ids[1]], canvas.SelectedElementIds);

            // Complete an empty marquee before switching back to the pencil.
            var blank = new SKPoint(200, 200);
            select.HandlePointerClick(blank);
            select.HandlePointerRelease(blank, blank);
            Assert.False(overlay.IsVisible);
            editor.UiStateViewModel.ActivePointerTool = pencil;
            DrawStroke(pencil, new SKPoint(10, 120), new SKPoint(35, 145));

            editor.UndoCommand.Execute(null);
            Assert.Equal(ids, canvas.CanvasElements.Select(element => element.Id));
            Assert.False(overlay.IsVisible);

            // Undo the deselection, then step back through the two original selections.
            foreach (var expected in ids.Reverse())
            {
                editor.UndoCommand.Execute(null);
                Assert.Multiple(
                    () => Assert.Equal([expected], canvas.SelectedElementIds),
                    () => Assert.True(overlay.IsVisible, "The restored stroke should have visible selection handles."));
            }

            editor.RedoCommand.Execute(null);
            Assert.Equal([ids[1]], canvas.SelectedElementIds);
            editor.RedoCommand.Execute(null);
            Assert.Empty(canvas.SelectedElementIds);
            Assert.False(overlay.IsVisible);

            // A reverse drag replaces the first selection in a single undo step.
            editor.UiStateViewModel.ActivePointerTool = select;
            select.SelectElements([(ISelectable)canvas.CanvasElements[0]]);
            var start = new SKPoint(100, 60);
            var end = new SKPoint(75, 35);
            select.HandlePointerClick(start);
            select.HandlePointerMove(start, end);
            select.HandlePointerRelease(start, end);
            Assert.Equal([ids[1]], canvas.SelectedElementIds);
            editor.UndoCommand.Execute(null);
            Assert.Equal([ids[0]], canvas.SelectedElementIds);
            Assert.True(overlay.IsVisible);
        }
        finally
        {
            await alice.LeaveRoomAsync("Alice");
            await bob.LeaveRoomAsync("Bob");
            window.Close();
        }
    }

    /// <summary>Completes a pencil gesture so the stroke has its own undo entry.</summary>
    private static void DrawStroke(PencilTool pencil, SKPoint start, SKPoint end)
    {
        pencil.HandlePointerClick(start);
        pencil.HandlePointerMove(start, end);
        pencil.HandlePointerRelease(start, end);
    }
}