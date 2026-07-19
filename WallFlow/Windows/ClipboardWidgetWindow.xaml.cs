using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using WallFlow.Services;

namespace WallFlow.Windows;

public partial class ClipboardWidgetWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public static ClipboardWidgetWindow? Instance { get; private set; }

    public ClipboardWidgetWindow()
    {
        InitializeComponent();
        Instance = this;
    }

    private bool _isDragging = false;
    private Point _clickPosition;
    private Point _dragStartScreenPoint;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance;

        // Set owner to DockWindow to exclude from Alt-Tab natively
        if (DockWindow.Instance != null)
        {
            Owner = DockWindow.Instance;
        }

        // Position window
        if (s.WidgetClipboardX >= 0 && s.WidgetClipboardY >= 0)
        {
            Left = s.WidgetClipboardX;
            Top = s.WidgetClipboardY;
        }
        else
        {
            // Default position: Right-bottom area of work area
            Left = SystemParameters.WorkArea.Right - Width - 30;
            Top = SystemParameters.WorkArea.Bottom - Height - 140;
        }

        // Clip bounds to screen area
        Left = Math.Clamp(Left, 0, SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(Top, 0, SystemParameters.VirtualScreenHeight - Height);

        // Wire dragging events programmatically to avoid snapping
        MouseLeftButtonUp += Window_MouseLeftButtonUp;
        MouseMove += Window_MouseMove;

        ClipboardService.Instance.HistoryChanged += OnClipboardHistoryChanged;
    }



    private void OnClipboardHistoryChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnClipboardHistoryChanged));
            return;
        }

        // 1. Get global mouse cursor position (where copy event occurred)
        POINT mousePos;
        if (GetCursorPos(out mousePos))
        {
            // 2. Center of this floating widget
            double targetX = Left + Width / 2;
            double targetY = Top + Height / 2;

            // 3. Spawn spiral energy particle window
            ClipboardSpiralWindow.Spawn(mousePos.X, mousePos.Y, targetX, targetY);
        }

        // 4. Play visual absorption pulse on widget front card
        TriggerPulseAnimation();
    }

    public void TriggerPulseAnimation()
    {
        // Pulse rotate
        var rotateAnim = new DoubleAnimation(FrontCardRotate.Angle, FrontCardRotate.Angle + 360, new Duration(TimeSpan.FromMilliseconds(500)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        FrontCardRotate.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

        // Pulse glow
        var glowAnim = new DoubleAnimation(0.4, 0.9, new Duration(TimeSpan.FromMilliseconds(250))) { AutoReverse = true };
        GlowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

        // Scale pop
        var scalePopX = new DoubleAnimation(1.0, 1.25, new Duration(TimeSpan.FromMilliseconds(200))) { AutoReverse = true, EasingFunction = new BackEase { Amplitude = 0.5 } };
        var scalePopY = new DoubleAnimation(1.0, 1.25, new Duration(TimeSpan.FromMilliseconds(200))) { AutoReverse = true, EasingFunction = new BackEase { Amplitude = 0.5 } };
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, scalePopX);
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, scalePopY);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDragging = true;
            _clickPosition = e.GetPosition(this);
            POINT p;
            GetCursorPos(out p);
            _dragStartScreenPoint = new Point(p.X, p.Y);
            CaptureMouse();
            e.Handled = true;
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            POINT p;
            if (GetCursorPos(out p))
            {
                // Move window coordinates manually to bypass Aero Snap Maximize triggers completely
                Left = p.X - _clickPosition.X;
                Top = p.Y - _clickPosition.Y;
            }
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();

            // Save new position
            var s = SettingsService.Instance;
            s.WidgetClipboardX = Left;
            s.WidgetClipboardY = Top;
            SettingsService.Save();

            // Check if it was a quick click rather than a drag movement
            POINT p;
            GetCursorPos(out p);
            double dist = Math.Sqrt(Math.Pow(p.X - _dragStartScreenPoint.X, 2) + Math.Pow(p.Y - _dragStartScreenPoint.Y, 2));
            if (dist < 4)
            {
                ExpandToHub();
            }
        }
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Handled by single-click in MouseLeftButtonUp, keeping stub to satisfy XAML definition
    }

    private void RootBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        if (FindResource("HoverStart") is Storyboard sb)
        {
            sb.Begin(this);
        }
    }

    private void RootBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        if (FindResource("HoverEnd") is Storyboard sb)
        {
            sb.Begin(this);
        }
    }

    private void ExpandToHub()
    {
        // Toggle the main ClipboardWindow
        DockWindow.Instance?.ToggleClipboard();
    }

    protected override void OnClosed(EventArgs e)
    {
        ClipboardService.Instance.HistoryChanged -= OnClipboardHistoryChanged;
        Instance = null;
        base.OnClosed(e);
    }
}
