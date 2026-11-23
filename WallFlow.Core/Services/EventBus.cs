using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;

namespace WallFlow.Services;

public interface IEvent { }

public class SettingsChangedEvent : IEvent { }

public class PreviewColorsRequestedEvent : IEvent { }

public class WidgetToggledEvent : IEvent
{
    public string WidgetName { get; }
    public WidgetToggledEvent(string name) => WidgetName = name;
}

public class WallpaperChangedEvent : IEvent
{
    public string WallpaperPath { get; }
    public WallpaperChangedEvent(string path) => WallpaperPath = path;
}

public class GalleryRefreshRequestedEvent : IEvent { }

public class WallpaperDirectoryChangedEvent : IEvent
{
    public string NewDirectory { get; }
    public WallpaperDirectoryChangedEvent(string directory) => NewDirectory = directory;
}

/// <summary>
/// High-performance, thread-safe EventBus bridging lightweight action subscriptions
/// and CommunityToolkit.Mvvm's WeakReferenceMessenger.
/// </summary>
public static class EventBus
{
    private static readonly Dictionary<Type, List<WeakAction>> Subscribers = new();
    private static readonly object _lock = new();

    public static WeakReferenceMessenger Messenger => WeakReferenceMessenger.Default;

    private sealed class WeakAction
    {
        private readonly WeakReference? _targetRef;
        private readonly MethodInfo _method;
        private readonly Delegate? _staticDelegate;

        public bool IsAlive => _staticDelegate != null || (_targetRef != null && _targetRef.IsAlive);

        public WeakAction(Delegate del)
        {
            _method = del.Method;
            if (del.Target != null)
            {
                _targetRef = new WeakReference(del.Target);
            }
            else
            {
                _staticDelegate = del;
            }
        }

        public bool Matches(Delegate del)
        {
            if (_staticDelegate != null)
                return Equals(_staticDelegate, del);

            return _targetRef?.Target != null &&
                   ReferenceEquals(_targetRef.Target, del.Target) &&
                   _method.Equals(del.Method);
        }

        public void Invoke(object @event)
        {
            if (_staticDelegate != null)
            {
                _staticDelegate.DynamicInvoke(@event);
            }
            else if (_targetRef?.Target is { } target)
            {
                _method.Invoke(target, new[] { @event });
            }
        }
    }

    public static void Subscribe<TRecipient, TEvent>(TRecipient recipient, Action<TRecipient, TEvent> handler)
        where TRecipient : class
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(handler);
        WeakReferenceMessenger.Default.Register<TRecipient, TEvent>(recipient, (r, m) => handler(r, m));
    }

    public static void Unsubscribe<TRecipient, TEvent>(TRecipient recipient)
        where TRecipient : class
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(recipient);
        WeakReferenceMessenger.Default.Unregister<TEvent>(recipient);
    }

    public static void Subscribe<TEvent>(Action<TEvent> callback) where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(callback);
        var type = typeof(TEvent);
        lock (_lock)
        {
            if (!Subscribers.TryGetValue(type, out var list))
            {
                list = new List<WeakAction>();
                Subscribers[type] = list;
            }
            list.Add(new WeakAction(callback));
        }
    }

    public static void Unsubscribe<TEvent>(Action<TEvent> callback) where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(callback);
        var type = typeof(TEvent);
        lock (_lock)
        {
            if (Subscribers.TryGetValue(type, out var list))
            {
                list.RemoveAll(wa => wa.Matches(callback));
                if (list.Count == 0)
                {
                    Subscribers.Remove(type);
                }
            }
        }
    }

    public static void Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        var type = typeof(TEvent);
        List<WeakAction>? copy = null;

        lock (_lock)
        {
            if (Subscribers.TryGetValue(type, out var list))
            {
                list.RemoveAll(wa => !wa.IsAlive);
                if (list.Count > 0)
                {
                    copy = new List<WeakAction>(list);
                }
            }
        }

        if (copy != null)
        {
            foreach (var action in copy)
            {
                try
                {
                    action.Invoke(@event);
                }
                catch { /* ignored */ }
            }
        }

        // Bridge to WeakReferenceMessenger for modern MVVM components
        MessengerPublisher<TEvent>.Publish(@event);
    }

    private static class MessengerPublisher<TEvent> where TEvent : IEvent
    {
        private static readonly Action<TEvent>? _publisher = Init();

        private static Action<TEvent>? Init()
        {
            if (!typeof(TEvent).IsClass) return null;
            try
            {
                var sendMethod = typeof(IMessengerExtensions)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "Send" && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2)
                    .MakeGenericMethod(typeof(TEvent));

                var param = System.Linq.Expressions.Expression.Parameter(typeof(TEvent), "e");
                var messengerInstance = System.Linq.Expressions.Expression.Constant(WeakReferenceMessenger.Default, typeof(IMessenger));
                var call = System.Linq.Expressions.Expression.Call(sendMethod, messengerInstance, param);
                return System.Linq.Expressions.Expression.Lambda<Action<TEvent>>(call, param).Compile();
            }
            catch
            {
                return null;
            }
        }

        public static void Publish(TEvent @event)
        {
            try
            {
                _publisher?.Invoke(@event);
            }
            catch { /* ignored */ }
        }
    }
}
