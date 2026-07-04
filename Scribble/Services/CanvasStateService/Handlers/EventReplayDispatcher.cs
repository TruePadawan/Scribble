using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Scribble.Services.CanvasStateService.Context;
using Scribble.Shared.Lib;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Builds a Type-to-handler lookup from event handler instances using reflections
/// and provides Dispatch (for replay) and TryFastPath (for fast-path optimization).
/// </summary>
public class EventReplayDispatcher
{
    private readonly Dictionary<Type, Action<Event, ReplayContext>> _replayHandlers = new();
    private readonly Dictionary<Type, Func<Event, FastPathContext, bool>> _fastPathHandlers = new();

    public EventReplayDispatcher(params object[] handlerInstances)
    {
        foreach (var instance in handlerInstances)
        {
            RegisterHandlers(instance);
        }
    }

    /// <summary>
    /// Dispatch a replay event to its registered handler.
    /// Silently skips events with no registered handler (e.g. UndoEvent, RedoEvent).
    /// </summary>
    public void Dispatch(Event @event, ReplayContext ctx)
    {
        if (_replayHandlers.TryGetValue(@event.GetType(), out var handler))
        {
            handler(@event, ctx);
        }
    }

    /// <summary>
    /// Attempt to apply a fast-path optimization for the event.
    /// Returns true if the fast-path was applied (skipping full replay).
    /// </summary>
    public bool TryFastPath(Event @event, FastPathContext ctx)
    {
        if (_fastPathHandlers.TryGetValue(@event.GetType(), out var handler))
        {
            return handler(@event, ctx);
        }

        return false;
    }

    /// <summary>
    /// Validates that every concrete Event subtype in the given assembly
    /// has a registered replay handler. Throws if any are missing.
    /// UndoEvent and RedoEvent are excluded as they are handled in the pre-pass.
    /// </summary>
    public void ValidateCompleteness(Assembly eventAssembly)
    {
        var allEventTypes = eventAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Event)) && !t.IsAbstract)
            .Where(t => t != typeof(UndoEvent) && t != typeof(RedoEvent))
            .ToList();

        var missing = allEventTypes
            .Where(t => !_replayHandlers.ContainsKey(t))
            .Select(t => t.Name)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing replay handlers for: {string.Join(", ", missing)}");
        }
    }

    private void RegisterHandlers(object instance)
    {
        var instanceType = instance.GetType();

        foreach (var iface in instanceType.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;

            var genericDef = iface.GetGenericTypeDefinition();

            if (genericDef == typeof(IEventReplayHandler<>))
            {
                var eventType = iface.GetGenericArguments()[0];
                var method = iface.GetMethod("Replay")!;
                var compiledDelegate = BuildReplayDelegate(instance, method, eventType);
                _replayHandlers[eventType] = compiledDelegate;
            }
            else if (genericDef == typeof(IFastPathHandler<>))
            {
                var eventType = iface.GetGenericArguments()[0];
                var method = iface.GetMethod("TryApplyFastPath")!;
                var compiledDelegate = BuildFastPathDelegate(instance, method, eventType);
                _fastPathHandlers[eventType] = compiledDelegate;
            }
        }
    }

    /// <summary>
    /// Builds a compiled delegate that casts the Event to the concrete type
    /// and calls the handler's Replay method, avoiding reflection on every dispatch.
    /// </summary>
    private static Action<Event, ReplayContext> BuildReplayDelegate(
        object instance, MethodInfo method, Type eventType)
    {
        var eventParam = Expression.Parameter(typeof(Event), "e");
        var ctxParam = Expression.Parameter(typeof(ReplayContext), "ctx");
        var castEvent = Expression.Convert(eventParam, eventType);
        var instanceConst = Expression.Constant(instance);
        var call = Expression.Call(instanceConst, method, castEvent, ctxParam);
        return Expression.Lambda<Action<Event, ReplayContext>>(call, eventParam, ctxParam).Compile();
    }

    /// <summary>
    /// Builds a compiled delegate for fast-path handlers.
    /// </summary>
    private static Func<Event, FastPathContext, bool> BuildFastPathDelegate(
        object instance, MethodInfo method, Type eventType)
    {
        var eventParam = Expression.Parameter(typeof(Event), "e");
        var ctxParam = Expression.Parameter(typeof(FastPathContext), "ctx");
        var castEvent = Expression.Convert(eventParam, eventType);
        var instanceConst = Expression.Constant(instance);
        var call = Expression.Call(instanceConst, method, castEvent, ctxParam);
        return Expression.Lambda<Func<Event, FastPathContext, bool>>(call, eventParam, ctxParam).Compile();
    }
}