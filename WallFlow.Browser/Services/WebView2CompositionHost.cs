using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WallFlow.Services;

public class WebView2CompositionHost : HwndHost
{
    private HwndSource? _hwndSource;
    private HostVisual? _hostVisual;
    private ContainerVisual? _rootVisual;

    public HostVisual? HostVisual => _hostVisual;

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WM_SIZE = 0x0005;

    public event Action<double, double>? HostResized;

    [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll", EntryPoint = "SetWindowRgn")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    public void UpdateClip(double width, double height, double cornerRadius)
    {
        if (_hostVisual != null && width > 0 && height > 0)
        {
            _hostVisual.Clip = new RectangleGeometry(new Rect(0, 0, width, height), cornerRadius, cornerRadius);
        }

        if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero && width > 0 && height > 0)
        {
            try
            {
                double dpiScale = 1.0;
                try { dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }

                int physicalWidth = (int)Math.Round(width * dpiScale);
                int physicalHeight = (int)Math.Round(height * dpiScale);
                int physicalRadius = (int)Math.Round(cornerRadius * dpiScale);

                IntPtr hRgn = CreateRoundRectRgn(0, 0, physicalWidth, physicalHeight, physicalRadius * 2, physicalRadius * 2);
                if (hRgn != IntPtr.Zero)
                {
                    SetWindowRgn(_hwndSource.Handle, hRgn, true);
                }
            }
            catch { }
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostVisual = new HostVisual();
        _rootVisual = new ContainerVisual();
        _rootVisual.Children.Add(_hostVisual);

        var parameters = new HwndSourceParameters(nameof(WebView2CompositionHost))
        {
            ParentWindow = hwndParent.Handle,
            UsesPerPixelOpacity = true,
            WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.RootVisual = _rootVisual;

        return new HandleRef(this, _hwndSource.Handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_hwndSource != null)
        {
            _hwndSource.RootVisual = null;
            _hwndSource.Dispose();
            _hwndSource = null;
        }
        _rootVisual = null;
        _hostVisual = null;
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZE)
        {
            int physicalWidth = (int)(short)(lParam.ToInt32() & 0xFFFF);
            int physicalHeight = (int)(short)((lParam.ToInt32() >> 16) & 0xFFFF);

            double dpiScale = 1.0;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                dpiScale = dpi.DpiScaleX;
            }
            catch { }

            double logicalWidth = physicalWidth / dpiScale;
            double logicalHeight = physicalHeight / dpiScale;

            HostResized?.Invoke(physicalWidth, physicalHeight);
            UpdateClip(logicalWidth, logicalHeight, 8);
        }
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }
}
