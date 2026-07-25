using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WallFlow.Services;

namespace WallFlow.Windows;

public partial class PowerWindow : Window, IWidget
{
    string IWidget.Name => "Power";
    public void Open() { Show(); Activate(); }
    public bool IsOpen => IsLoaded && IsVisible;
    public PowerWindow()
    {
        InitializeComponent();
    }

    private bool _isClosing = false;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Position and size to cover the entire selected screen bounds (to avoid standard maximized borders)
        var screens = System.Windows.Forms.Screen.AllScreens;
        var screenIndex = Math.Clamp(SettingsService.Instance.DockMonitorIndex, 0, screens.Length - 1);
        var screen = screens[screenIndex];
        var dpi = VisualTreeHelper.GetDpi(this);

        Left = screen.Bounds.X / dpi.DpiScaleX;
        Top = screen.Bounds.Y / dpi.DpiScaleY;
        Width = screen.Bounds.Width / dpi.DpiScaleX;
        Height = screen.Bounds.Height / dpi.DpiScaleY;

        BackdropHelper.ApplyAcrylic(this, false);
        BackdropHelper.UpdateWindowRegion(this, 0);

        // Simple scale + fade animation on MainPanel instead of the Window itself
        if (MainPanel != null)
        {
            MainPanel.RenderTransformOrigin = new Point(0.5, 0.5);
            var tg = new TransformGroup();
            var scale = new ScaleTransform(0.9, 0.9);
            tg.Children.Add(scale);
            MainPanel.RenderTransform = tg;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            var scaleUpX = new System.Windows.Media.Animation.DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            var scaleUpY = new System.Windows.Media.Animation.DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeIn);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUpX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUpY);
        }
    }

    private void SafeClose(Action? onClosedAction = null)
    {
        if (_isClosing) return;
        _isClosing = true;

        if (MainPanel != null && MainPanel.RenderTransform is TransformGroup tg && tg.Children.Count > 0 && tg.Children[0] is ScaleTransform scale)
        {
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            var scaleDownX = new System.Windows.Media.Animation.DoubleAnimation(0.9, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            var scaleDownY = new System.Windows.Media.Animation.DoubleAnimation(0.9, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, args) =>
            {
                Close();
                onClosedAction?.Invoke();
            };

            BeginAnimation(OpacityProperty, fadeOut);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDownX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDownY);
        }
        else
        {
            Close();
            onClosedAction?.Invoke();
        }
    }

    private void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        SafeClose(() =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = "/s /t 0", CreateNoWindow = true, UseShellExecute = false }); }
            catch { }
        });
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        SafeClose(() =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = "/r /t 0", CreateNoWindow = true, UseShellExecute = false }); }
            catch { }
        });
    }

    private void Logoff_Click(object sender, RoutedEventArgs e)
    {
        SafeClose(() =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = "/l", CreateNoWindow = true, UseShellExecute = false }); }
            catch { }
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SafeClose();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SafeClose();
        }
    }
}
