using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Scribble.Extensions;
using Scribble.Services.CanvasStateService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.ViewModels;
using Scribble.Views;

namespace Scribble.UnitTests.ViewModelTests;

/// <summary>
/// Exercise shortcut routing and service subscriptions through the editor's actual lifecycle.
/// </summary>
public class MainViewLifetimeTests
{
    // Replacing, clearing, and reattaching the context must leave just one active editor.
    [AvaloniaFact]
    public void RebindAndReattach_OnlyCurrentEditorReceivesShortcutsAndUpdates()
    {
        var canvas = Substitute.For<ICanvasStateService>();
        canvas.GetSelectedElements().Returns([]);
        var room = Substitute.For<IMultiUserDrawingService>();
        var registrations = new ServiceCollection();
        registrations.AddCommonServices();
        registrations.AddSingleton(canvas);
        registrations.AddSingleton(room);
        using var services = registrations.BuildServiceProvider();
        var first = services.GetRequiredService<MainViewModel>();
        var second = services.GetRequiredService<MainViewModel>();
        var view = new MainView(services) { DataContext = first };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var firstRefreshes = 0;
            var secondRefreshes = 0;
            first.RequestRefreshSelection += (_, _) => firstRefreshes++;
            second.RequestRefreshSelection += (_, _) => secondRefreshes++;
            var shortcuts = view.KeyBindings.Count;
            view.DataContext = second;
            var initialRefreshes = secondRefreshes;

            // A service event reaches only the active model and a tool shortcut selects its tool.
            canvas.SelectionInvalidated += Raise.Event<Action<IReadOnlyList<Guid>, bool>>(Array.Empty<Guid>(), false);
            firstRefreshes.Should().Be(0);
            secondRefreshes.Should().Be(initialRefreshes + 1);
            room.MessageReceived += Raise.Event<Action<Message>>(new Message("id", "sender", "Alice", "hello"));
            first.MultiUserDrawingViewModel.Should().BeSameAs(second.MultiUserDrawingViewModel);
            second.MultiUserDrawingViewModel.Messages.Should().ContainSingle();
            view.FocusCanvas();
            window.KeyPress(Key.V, RawInputModifiers.None, PhysicalKey.V, "v");
            second.UiStateViewModel.ActivePointerTool!.Name.Should().Be("SelectTool");
            first.UiStateViewModel.ActivePointerTool.Should().BeNull();

            // Reactivating the old model elsewhere must not redraw this view's quick-select borders.
            canvas.ClearReceivedCalls();
            first.Activate();
            _ = canvas.Received(1).CanvasElements;
            first.Deactivate();

            // Removing the view or clearing its context must silence the model.
            window.Content = null;
            var detachedRefreshes = secondRefreshes;
            canvas.SelectionInvalidated += Raise.Event<Action<IReadOnlyList<Guid>, bool>>(Array.Empty<Guid>(), false);
            secondRefreshes.Should().Be(detachedRefreshes);
            room.MessageReceived += Raise.Event<Action<Message>>(new Message("id2", "sender", "Alice", "still here"));
            window.Content = view;
            window.UpdateLayout();
            second.MultiUserDrawingViewModel.Messages.Should().HaveCount(2);
            second.UiStateViewModel.ActivePointerTool!.Name.Should().Be("SelectTool");
            view.DataContext = null;
            var clearedRefreshes = secondRefreshes;
            canvas.SelectionInvalidated += Raise.Event<Action<IReadOnlyList<Guid>, bool>>(Array.Empty<Guid>(), false);
            secondRefreshes.Should().Be(clearedRefreshes);

            view.DataContext = second;
            view.KeyBindings.Count.Should().Be(shortcuts);
            view.FocusCanvas();
            canvas.ClearReceivedCalls();
            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            canvas.Received(1).GetSelectedElements();
        }
        finally
        {
            window.Close();
        }
    }
}