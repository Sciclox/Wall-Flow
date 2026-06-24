using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using Microsoft.Win32;

namespace WallFlow;

public partial class ContextMenuWindow : Window
{
    private static ContextMenuWindow? _openInstance;
    private bool _autoStartEnabled;
    private bool _closedByAction;
    private bool _wasActivated;
    private DateTime _shownAt;
    private IntPtr _windowHandle;
    private double _cursorTopDips;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_ENABLE_HOSTBACKDROP = 5,
        ACCENT_INVALID_STATE = 6
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    public ContextMenuWindow()
    {
        InitializeComponent();
        Activated += (_, _) => _wasActivated = true;
        _autoStartEnabled = IsAutoStartEnabled();
        UpdateCheckState();
    }

    public void ShowAtCursor()
    {
        _openInstance?.Close();
        _openInstance = this;
        _shownAt = DateTime.UtcNow;
        _closedByAction = false;
        _wasActivated = false;

        GetCursorPos(out var pt);
        var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0)
        {
            Left = pt.X / (dpiX / 96.0);
            _cursorTopDips = pt.Y / (dpiY / 96.0);
            Top = _cursorTopDips;
        }
        else
        {
            Left = pt.X;
            _cursorTopDips = pt.Y;
            Top = pt.Y;
        }

        base.Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        HideFromAltTab();
        EnableBlur();
    }

    private void HideFromAltTab()
    {
        try
        {
            var exStyle = GetWindowLong(_windowHandle, GWL_EXSTYLE);
            SetWindowLong(_windowHandle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private void EnableBlur()
    {
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(accent, ptr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            Data = ptr,
            SizeOfData = size
        };

        SetWindowCompositionAttribute(_windowHandle, ref data);
        Marshal.FreeHGlobal(ptr);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _shownAt = DateTime.UtcNow;

        Top = Math.Max(0, _cursorTopDips - ActualHeight);

        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        var scaleIn = new DoubleAnimation(0.92, 1.0, duration) { EasingFunction = ease };

        BeginAnimation(OpacityProperty, fadeIn);
        if (RootBorder.RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
        }
    }

    private void UpdateCheckState()
    {
        CheckMark.Visibility = _autoStartEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Item_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border b)
        {
            var colorAnimation = new ColorAnimation(
                Color.FromRgb(0x3A, 0x3A, 0x3A),
                TimeSpan.FromMilliseconds(150));
            b.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            b.Background.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
        }
    }

    private void Item_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border b)
            b.Background = Brushes.Transparent;
    }

    private void AutoStart_Click(object sender, MouseButtonEventArgs e)
    {
        ToggleAutoStart();
        _autoStartEnabled = IsAutoStartEnabled();
        UpdateCheckState();
        e.Handled = true;
    }

    private void Exit_Click(object sender, MouseButtonEventArgs e)
    {
        _closedByAction = true;
        _openInstance = null;
        Application.Current.Shutdown();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_closedByAction) return;
        if (!_wasActivated) return;
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 300) return;
        
        CloseMenu();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        // Cerrar el menú cuando el ratón sale de la ventana
        if (_wasActivated && (DateTime.UtcNow - _shownAt).TotalMilliseconds > 300)
        {
            CloseMenu();
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Permitir que los clics dentro del menú se manejen normalmente
        e.Handled = false;
    }

    private void CloseMenu()
    {
        if (_openInstance == this)
            _openInstance = null;
        
        var duration = TimeSpan.FromMilliseconds(150);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var fadeOut = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        var scaleOut = new DoubleAnimation(1.0, 0.92, duration) { EasingFunction = ease };

        fadeOut.Completed += (_, _) => Close();

        BeginAnimation(OpacityProperty, fadeOut);
        if (RootBorder.RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
        }
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("WallFlow") != null;
    }

    private static void ToggleAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null) return;

        if (key.GetValue("WallFlow") != null)
            key.DeleteValue("WallFlow", false);
        else
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
                key.SetValue("WallFlow", $"\"{path}\"");
        }
    }
}
