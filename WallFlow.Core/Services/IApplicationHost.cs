using System;

namespace WallFlow.Services
{
    public interface IApplicationHost
    {
        double Left { get; }
        double Top { get; }
        double ActualWidth { get; }
        double ActualHeight { get; }
        bool IsPinnedFlyoutOpen { get; set; }
        void RecordWidgetClose(string widgetType);
    }
}
