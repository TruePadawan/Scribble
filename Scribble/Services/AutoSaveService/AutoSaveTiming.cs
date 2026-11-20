using System;
using System.Threading;

namespace Scribble.Services.AutoSaveService;

/// <summary>Provides monotonic time and one-shot timers for recovery scheduling.</summary>
internal interface IAutoSaveClock
{
    /// <summary>Reads the current monotonic timestamp in milliseconds.</summary>
    long GetTimestampMilliseconds();

    /// <summary>Creates a one-shot timer that invokes the supplied callback.</summary>
    IAutoSaveTimer CreateTimer(Action callback);
}

/// <summary>Changes or disposes a single autosave timer.</summary>
internal interface IAutoSaveTimer : IDisposable
{
    /// <summary>Sets the next firing delay or disables the timer.</summary>
    void Change(TimeSpan dueTime);
}

/// <summary>Uses the system monotonic clock and timer in production.</summary>
internal sealed class SystemAutoSaveClock : IAutoSaveClock
{
    /// <summary>Returns elapsed monotonic milliseconds.</summary>
    public long GetTimestampMilliseconds() => Environment.TickCount64;

    /// <summary>Creates a one-shot system timer for an autosave callback.</summary>
    public IAutoSaveTimer CreateTimer(Action callback) => new SystemAutoSaveTimer(callback);
}

/// <summary>Adapts a system timer to the autosave timer contract.</summary>
internal sealed class SystemAutoSaveTimer : IAutoSaveTimer
{
    private readonly Timer _timer;

    /// <summary>Creates an idle timer that invokes the supplied callback when armed.</summary>
    public SystemAutoSaveTimer(Action callback)
    {
        _timer = new Timer(_ => callback(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Arms a one-shot timer or disables it when given the infinite delay.</summary>
    public void Change(TimeSpan dueTime) => _timer.Change(dueTime, Timeout.InfiniteTimeSpan);

    /// <summary>Releases the underlying system timer.</summary>
    public void Dispose() => _timer.Dispose();
}