using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WallFlow;

internal class TrayIcon : IDisposable
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint cmd, ref NOTIFYICONDATA data);

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;

    private const uint NIF_MESSAGE = 0x0001;
    private const uint NIF_ICON = 0x0002;
    private const uint NIF_TIP = 0x0004;

    private const uint WM_NOTIFYICON = 0x8000;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private NOTIFYICONDATA _nid;
    private HwndSource? _source;
    private Icon? _icon;
    private bool _added;
    private bool _disposed;

    private static uint _nextId = 100;
    private readonly uint _id;

    public event Action? LeftClick;
    public event Action? RightClick;

    public TrayIcon()
    {
        _id = _nextId++;

        var param = new HwndSourceParameters("WallFlowTray_" + _id)
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(param);
        _source.AddHook(WndProc);

        _nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = _id,
            uFlags = NIF_MESSAGE,
            uCallbackMessage = WM_NOTIFYICON
        };
    }

    public void SetIcon(Icon icon)
    {
        _icon = icon;
        _nid.hIcon = icon.Handle;
        _nid.uFlags |= NIF_ICON;

        if (_added)
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    public string Text
    {
        set
        {
            _nid.szTip = value ?? "";
            _nid.uFlags |= NIF_TIP;

            if (_added)
                Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }
    }

    public void Show()
    {
        if (_added) return;
        _added = Shell_NotifyIcon(NIM_ADD, ref _nid);
    }

    public void Hide()
    {
        if (!_added) return;
        Shell_NotifyIcon(NIM_DELETE, ref _nid);
        _added = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NOTIFYICON)
        {
            var evt = (uint)lParam;

            if (evt == WM_LBUTTONUP)
            {
                LeftClick?.Invoke();
                handled = true;
            }
            else if (evt == WM_RBUTTONUP)
            {
                RightClick?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Hide();

        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        _icon?.Dispose();
    }
}
