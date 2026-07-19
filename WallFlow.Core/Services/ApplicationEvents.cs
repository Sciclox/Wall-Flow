using System;

namespace WallFlow.Services
{
    public static class ApplicationEvents
    {
        public static event Action? RefreshRequested;
        public static event Action<string>? DirectoryChanged;

        public static void NotifyRefreshRequested()
        {
            RefreshRequested?.Invoke();
        }

        public static void NotifyDirectoryChanged(string path)
        {
            DirectoryChanged?.Invoke(path);
        }
    }
}
