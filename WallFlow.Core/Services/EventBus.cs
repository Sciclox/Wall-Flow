using System;
using System.Collections.Generic;

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

public static class EventBus
{
    private static readonly Dictionary<Type, List<object>> Subscribers = new();

    public static void Subscribe<TEvent>(Action<TEvent> callback) where TEvent : IEvent
    {
        var type = typeof(TEvent);
        if (!Subscribers.TryGetValue(type, out var list))
        {
            list = new List<object>();
            Subscribers[type] = list;
        }
        list.Add(callback);
    }

    public static void Unsubscribe<TEvent>(Action<TEvent> callback) where TEvent : IEvent
    {
        var type = typeof(TEvent);
        if (Subscribers.TryGetValue(type, out var list))
        {
            list.Remove(callback);
            if (list.Count == 0)
            {
                Subscribers.Remove(type);
            }
        }
    }

    public static void Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        var type = typeof(TEvent);
        if (Subscribers.TryGetValue(type, out var list))
        {
            var callbacks = new List<object>(list);
            foreach (var callback in callbacks)
            {
                try
                {
                    ((Action<TEvent>)callback)(@event);
                }
                catch { /* ignored */ }
            }
        }
    }
}
