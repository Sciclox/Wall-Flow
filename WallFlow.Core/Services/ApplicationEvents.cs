using System;

namespace WallFlow.Services;

/// <summary>
/// Backward-compatible bridge forwarding legacy application events through EventBus.
/// </summary>
public static class ApplicationEvents
{
    public static event Action? RefreshRequested;
    public static event Action<string>? DirectoryChanged;

    static ApplicationEvents()
    {
        EventBus.Subscribe<GalleryRefreshRequestedEvent>(_ => RefreshRequested?.Invoke());
        EventBus.Subscribe<WallpaperDirectoryChangedEvent>(e => DirectoryChanged?.Invoke(e.NewDirectory));
    }

    public static void NotifyRefreshRequested()
    {
        EventBus.Publish(new GalleryRefreshRequestedEvent());
    }

    public static void NotifyDirectoryChanged(string path)
    {
        EventBus.Publish(new WallpaperDirectoryChangedEvent(path));
    }
}
