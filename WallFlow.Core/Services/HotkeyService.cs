using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace WallFlow.Services;

public class HotkeyService
{
    private static HotkeyService? _instance;
    public static HotkeyService Instance => _instance ??= new HotkeyService();

    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifier flags
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private HwndSource? _hwndSource;
    private IntPtr _hWnd;
    private readonly Dictionary<int, Action> _hotkeyActions = new();
    private readonly List<int> _registeredIds = new();
    private int _currentId = 1;

    public void Initialize(IntPtr hWnd)
    {
        if (_hWnd != IntPtr.Zero)
        {
            UnregisterAll();
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource = null;
            }
        }

        _hWnd = hWnd;
        _hwndSource = HwndSource.FromHwnd(_hWnd);
        if (_hwndSource != null)
        {
            _hwndSource.AddHook(HwndHook);
        }
    }

    public bool Register(string hotkeyText, Action callback)
    {
        if (string.IsNullOrWhiteSpace(hotkeyText) || _hWnd == IntPtr.Zero)
            return false;

        if (!ParseHotkey(hotkeyText, out uint modifiers, out uint vk))
            return false;

        int id = _currentId++;
        if (RegisterHotKey(_hWnd, id, modifiers | MOD_NOREPEAT, vk))
        {
            _hotkeyActions[id] = callback;
            _registeredIds.Add(id);
            return true;
        }
        return false;
    }

    public void UnregisterAll()
    {
        if (_hWnd == IntPtr.Zero)
            return;

        foreach (var id in _registeredIds)
        {
            UnregisterHotKey(_hWnd, id);
        }

        _registeredIds.Clear();
        _hotkeyActions.Clear();
        _currentId = 1;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_hotkeyActions.TryGetValue(id, out var action))
            {
                action?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private bool ParseHotkey(string hotkeyText, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        var parts = hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        string keyPart = parts[parts.Length - 1].Trim();

        // Modifiers
        for (int i = 0; i < parts.Length - 1; i++)
        {
            string mod = parts[i].Trim().ToLower();
            if (mod == "ctrl" || mod == "control")
                modifiers |= MOD_CONTROL;
            else if (mod == "shift")
                modifiers |= MOD_SHIFT;
            else if (mod == "alt")
                modifiers |= MOD_ALT;
            else if (mod == "win" || mod == "windows")
                modifiers |= MOD_WIN;
        }

        // Parse key
        if (Enum.TryParse<Key>(keyPart, true, out Key key))
        {
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return vk > 0;
        }

        // Fallbacks for special names
        if (keyPart.Equals("Esc", StringComparison.OrdinalIgnoreCase) || keyPart.Equals("Escape", StringComparison.OrdinalIgnoreCase))
            vk = 0x1B;
        else if (keyPart.Equals("Enter", StringComparison.OrdinalIgnoreCase) || keyPart.Equals("Return", StringComparison.OrdinalIgnoreCase))
            vk = 0x0D;
        else if (keyPart.Equals("Tab", StringComparison.OrdinalIgnoreCase))
            vk = 0x09;
        else if (keyPart.Equals("Space", StringComparison.OrdinalIgnoreCase))
            vk = 0x20;

        return vk > 0;
    }
}
