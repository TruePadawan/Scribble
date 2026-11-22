using NSubstitute;
using Scribble.Services.AutoSaveService;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DocumentService;
using Scribble.Services.MultiUserDrawing;
using SkiaSharp;
using FluentAssertions;

namespace Scribble.UnitTests.ServiceTests;

/// <summary>
/// Check autosave deadlines by advancing a manual clock instead of sleeping.
/// </summary>
public class AutoSaveClockTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumWait = TimeSpan.FromMilliseconds(300);

    // A later edit restarts the quiet period before the writer becomes eligible.
    [Fact]
    public async Task ContentChanged_UsesDebounceDeadline()
    {
        using var fixture = CreateFixture();

        fixture.Canvas.ContentChanged += Raise.Event<Action>();
        fixture.Clock.AdvanceBy(TimeSpan.FromMilliseconds(80));
        fixture.Canvas.ContentChanged += Raise.Event<Action>();
        fixture.Clock.AdvanceBy(TimeSpan.FromMilliseconds(99));
        fixture.SaveStarted.Task.IsCompleted.Should().BeFalse();

        fixture.Clock.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await fixture.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await WaitForRecoveryAsync(fixture.RecoveryPath);
    }

    // Continuous edits still publish at the maximum wait deadline.
    [Fact]
    public async Task ContinuousContentChanges_UsesMaximumWaitDeadline()
    {
        using var fixture = CreateFixture();

        fixture.Canvas.ContentChanged += Raise.Event<Action>();
        for (var i = 0; i < 3; i++)
        {
            fixture.Clock.AdvanceBy(TimeSpan.FromMilliseconds(80));
            fixture.Canvas.ContentChanged += Raise.Event<Action>();
        }

        fixture.Clock.AdvanceBy(TimeSpan.FromMilliseconds(59));
        fixture.SaveStarted.Task.IsCompleted.Should().BeFalse();
        fixture.Clock.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await fixture.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await WaitForRecoveryAsync(fixture.RecoveryPath);
    }

    // A failed write is retried after the normal debounce interval.
    [Fact]
    public async Task WriteFailure_RetriesAfterDebounceDeadline()
    {
        using var fixture = CreateFixture();
        var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        fixture.Document.SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstAttempt.TrySetResult();
                return Task.FromException(new IOException("Simulated failure"));
            }

            retryStarted.TrySetResult();
            return Task.CompletedTask;
        });

        fixture.Canvas.ContentChanged += Raise.Event<Action>();
        fixture.Clock.AdvanceBy(Debounce);
        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        fixture.Clock.AdvanceBy(Debounce - TimeSpan.FromMilliseconds(1));
        attempts.Should().Be(1);

        fixture.Clock.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await WaitForRecoveryAsync(fixture.RecoveryPath);
    }

    /// <summary>Waits for the asynchronous writer to finish publishing its temporary file.</summary>
    private static async Task WaitForRecoveryAsync(string path)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        while (!File.Exists(path)) await Task.Delay(10, timeout.Token);
    }

    /// <summary>Creates an autosave service wired to a manual clock and lightweight substitutes.</summary>
    private static Fixture CreateFixture()
    {
        var canvas = Substitute.For<ICanvasStateService>();
        var document = Substitute.For<IDocumentService>();
        var room = Substitute.For<IMultiUserDrawingService>();
        var clock = new ManualAutoSaveClock();
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        document.CaptureSnapshot().Returns(_ => new DocumentSnapshot([], SKColors.Black));
        document.SaveAsync(Arg.Any<Stream>(), Arg.Any<DocumentSnapshot>()).Returns(call =>
        {
            saveStarted.TrySetResult();
            return call.Arg<Stream>().WriteAsync("snapshot"u8.ToArray()).AsTask();
        });
        var service = new AutoSaveService(canvas, document, room, directory,
            debounceDelay: Debounce, maxDelay: MaximumWait, clock: clock);
        return new Fixture(service, canvas, document, clock, saveStarted, directory);
    }

    /// <summary>Owns the service substitutes and temporary recovery folder used by one test.</summary>
    private sealed class Fixture(AutoSaveService service, ICanvasStateService canvas, IDocumentService document,
        ManualAutoSaveClock clock, TaskCompletionSource saveStarted, string directory) : IDisposable
    {
        public AutoSaveService Service { get; } = service;
        public ICanvasStateService Canvas { get; } = canvas;
        public IDocumentService Document { get; } = document;
        public ManualAutoSaveClock Clock { get; } = clock;
        public TaskCompletionSource SaveStarted { get; } = saveStarted;
        public string RecoveryPath { get; } = Path.Combine(directory, "Scribble", "autosave.scribble");

        /// <summary>Stops pending timers before deleting the test recovery files.</summary>
        public void Dispose()
        {
            Service.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

/// <summary>Moves monotonic time forward and fires timers whose deadlines have arrived.</summary>
internal sealed class ManualAutoSaveClock : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualAutoSaveTimer> _timers = [];
    private long _now;

    /// <summary>Returns the test clock's current timestamp in milliseconds.</summary>
    public override long TimestampFrequency => 1000;

    /// <summary>
    /// Read the current manual timestamp.
    /// </summary>
    public override long GetTimestamp()
    {
        lock (_gate) return _now;
    }

    /// <summary>Creates an inactive timer controlled by this clock.</summary>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualAutoSaveTimer(this, () => callback(state));
        timer.Change(dueTime, period);
        lock (_gate) _timers.Add(timer);
        return timer;
    }

    /// <summary>Advances time and synchronously invokes callbacks due before the new time.</summary>
    public void AdvanceBy(TimeSpan elapsed)
    {
        lock (_gate) _now += (long)elapsed.TotalMilliseconds;
        while (true)
        {
            Action? callback = null;
            lock (_gate)
            {
                var dueTimer = _timers.FirstOrDefault(timer => timer.DueAt is { } dueAt && dueAt <= _now);
                if (dueTimer is not null)
                {
                    dueTimer.DueAt = null;
                    callback = dueTimer.Callback;
                }
            }

            if (callback is null) return;
            callback();
        }
    }

    /// <summary>Sets or disables one timer under the clock lock.</summary>
    private void Change(ManualAutoSaveTimer timer, TimeSpan dueTime)
    {
        lock (_gate)
        {
            timer.DueAt = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : _now + (long)dueTime.TotalMilliseconds;
        }
    }

    /// <summary>Removes a disposed timer from the manual clock.</summary>
    private void Remove(ManualAutoSaveTimer timer)
    {
        lock (_gate) _timers.Remove(timer);
    }

    /// <summary>Represents one timer owned by a manual clock.</summary>
    private sealed class ManualAutoSaveTimer(ManualAutoSaveClock clock, Action callback) : ITimer
    {
        public Action Callback { get; } = callback;
        public long? DueAt { get; set; }

        /// <summary>Updates this timer's due timestamp.</summary>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException();
            clock.Change(this, dueTime);
            return true;
        }

        /// <summary>
        /// Manual callbacks require no asynchronous teardown.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>Removes this timer from its clock.</summary>
        public void Dispose() => clock.Remove(this);
    }
}
