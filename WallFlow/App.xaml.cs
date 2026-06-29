using System;
using System.Drawing;
using System.Reflection;
using System.Windows;

namespace WallFlow;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mainWindow = new MainWindow();
        _mainWindow.Show();

        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("WallFlow.logofondo.ico");

        var icon = stream != null ? new Icon(stream) : SystemIcons.Application;

        _trayIcon = new TrayIcon();
        _trayIcon.SetIcon(icon);
        _trayIcon.Text = "WallFlow";
        _trayIcon.LeftClick += () =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
        };
        _trayIcon.RightClick += ShowContextMenu;
        _trayIcon.Show();
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenuWindow();
        menu.ShowAtCursor();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
