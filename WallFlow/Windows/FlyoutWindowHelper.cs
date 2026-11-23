using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WallFlow.Services;

namespace WallFlow.Windows;

/// <summary>
/// Centralized helper for positioning, animating, and styling auxiliary flyout windows
/// (Weather, Calendar, MediaFlyout, Power, etc.) consistently across multiple monitors.
/// </summary>
public static class FlyoutWindowHelper
{
    public static void ApplyFlyoutStyle(Window window, Border? rootBorder, double cornerRadius = 8)
    {
        BackdropHelper.ApplyAcrylic(window);
        BackdropHelper.UpdateWindowRegion(window, (int)cornerRadius);

        if (rootBorder != null)
        {
            void ClipHandler(object? s, RoutedEventArgs? e) => UpdateBorderClip(window, rootBorder, cornerRadius);
            rootBorder.SizeChanged += ClipHandler;
            UpdateBorderClip(window, rootBorder, cornerRadius);
        }
    }

    public static void UpdateBorderClip(Window window, Border rootBorder, double cornerRadius = 8)
    {
        if (rootBorder == null) return;
        var w = rootBorder.ActualWidth > 0 ? rootBorder.ActualWidth : window.Width;
        var h = rootBorder.ActualHeight > 0 ? rootBorder.ActualHeight : window.Height;
        if (w <= 0 || h <= 0) return;

        rootBorder.Clip = new RectangleGeometry(new Rect(0, 0, w, h), cornerRadius, cornerRadius);
        window.Clip = new RectangleGeometry(new Rect(0, 0, window.ActualWidth > 0 ? window.ActualWidth : w, window.ActualHeight > 0 ? window.ActualHeight : h), cornerRadius, cornerRadius);
        BackdropHelper.UpdateWindowRegion(window, (int)cornerRadius);
    }

    public static void PositionAndAnimateFlyout(Window window, double targetLeft, double slideDistance = 15, int durationMs = 250)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screenIndex = Math.Clamp(SettingsService.Instance.DockMonitorIndex, 0, screens.Length - 1);
        var screen = screens[screenIndex];
        var dpi = VisualTreeHelper.GetDpi(window);

        double screenLeft = screen.Bounds.X / dpi.DpiScaleX;
        double screenRight = (screen.Bounds.X + screen.Bounds.Width) / dpi.DpiScaleX;

        window.Left = targetLeft;

        // Ensure window stays within screen bounds
        if (window.Left < screenLeft) window.Left = screenLeft + 10;
        if (window.Left + window.Width > screenRight)
        {
            window.Left = screenRight - window.Width - 10;
        }

        bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
        double finalTop;
        if (isBottom)
        {
            finalTop = DockWindow.Instance != null 
                ? DockWindow.Instance.Top - window.Height 
                : (screen.Bounds.Y + screen.Bounds.Height) / dpi.DpiScaleY - window.Height - 56;
        }
        else
        {
            finalTop = DockWindow.Instance != null 
                ? DockWindow.Instance.Top + DockWindow.Instance.ActualHeight 
                : (screen.Bounds.Y / dpi.DpiScaleY) + 56;
        }

        double startTop = isBottom ? finalTop + slideDistance : finalTop - slideDistance;
        window.Top = startTop;
        window.Opacity = 0;

        var cubicEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = cubicEase
        };
        var slide = new DoubleAnimation(startTop, finalTop, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = cubicEase
        };

        window.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        window.BeginAnimation(Window.TopProperty, slide);
    }
}
