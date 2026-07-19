using System;

namespace WallFlow.Services
{
    public interface IApplicationHost
    {
        double Left { get; }
        double Top { get; }
        double ActualWidth { get; }
        double ActualHeight { get; }
        System.Windows.FrameworkElement? WebBrowserButton { get; }
        void UpdateBrowserOpenState();
        void OpenNewBrowserWindow(string? url = null);
        void OnBrowserFullScreenChanged(bool isFullScreen);
        void SaveBrowserSession();
        int GetBrowserWindowsCount();
        void AddBrowserWindow(System.Windows.Window window);
        bool IsPinnedFlyoutOpen { get; set; }
        void RecordWidgetClose(string widgetType);
    }
}
