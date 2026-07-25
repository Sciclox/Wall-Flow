using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WallFlow.Windows;

public partial class ClipboardSpiralWindow : Window
{
    private double _startX;
    private double _startY;
    private double _targetX;
    private double _targetY;
    private DateTime _startTime;
    private const double DurationMs = 700;

    // Win32 imports to force complete click-through and tool window styles
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public static void Spawn(double startX, double startY, double targetX, double targetY)
    {
        var win = new ClipboardSpiralWindow(startX, startY, targetX, targetY);
        win.Show();
    }

    public ClipboardSpiralWindow(double startX, double startY, double targetX, double targetY)
    {
        InitializeComponent();
        ShowActivated = false;
        
        _startX = startX;
        _startY = startY;
        _targetX = targetX;
        _targetY = targetY;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Hook native styles to ensure it behaves strictly as a click-through overlay
        var hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // 2. Adjust window dimensions to cover entire virtual screen area
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        // Convert starting coordinates from physical pixels (Win32 API) to device-independent WPF units
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (dpi.DpiScaleX > 0 && dpi.DpiScaleY > 0)
            {
                _startX /= dpi.DpiScaleX;
                _startY /= dpi.DpiScaleY;
            }
        }
        catch { }

        // Convert coordinates from absolute screen space to local window space
        _startX -= Left;
        _startY -= Top;
        _targetX -= Left;
        _targetY -= Top;

        // 3. Start render loop
        _startTime = DateTime.Now;
        CompositionTarget.Rendering += OnRenderTick;
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.Now - _startTime).TotalMilliseconds;
        double t = elapsed / DurationMs;

        if (t >= 1.0)
        {
            CompositionTarget.Rendering -= OnRenderTick;
            Close();
            return;
        }

        ParticleCanvas.Children.Clear();

        // Color setup
        Color accentColor = Color.FromRgb(0xAF, 0x52, 0xDE); // Fallback purple
        if (Application.Current.Resources.Contains("AccentColor"))
        {
            accentColor = (Color)Application.Current.Resources["AccentColor"];
        }

        // Draw expanding ripple at starting point (first half of animation)
        if (t <= 0.5)
        {
            double rippleSize = t * 160.0;
            double rippleOpacity = 1.0 - (t * 2.0);
            var ripple = new Ellipse
            {
                Width = rippleSize,
                Height = rippleSize,
                Stroke = new SolidColorBrush(accentColor),
                StrokeThickness = 2.0 * rippleOpacity,
                Opacity = rippleOpacity
            };
            
            ripple.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accentColor,
                BlurRadius = 15,
                ShadowDepth = 0,
                Opacity = rippleOpacity * 0.8
            };

            Canvas.SetLeft(ripple, _startX - rippleSize / 2);
            Canvas.SetTop(ripple, _startY - rippleSize / 2);
            ParticleCanvas.Children.Add(ripple);
        }

        // Draw particle tail (6 trail segments)
        int tailSteps = 6;
        for (int i = 0; i < tailSteps; i++)
        {
            double delay = i * 0.02; // delay factor for tail
            double ti = Math.Max(0.0, t - delay);

            // Interpolated center line
            double lerpX = _startX + (_targetX - _startX) * ti;
            double lerpY = _startY + (_targetY - _startY) * ti;

            // Spiral maths (tapering radius and fast rotation)
            double angle = ti * Math.PI * 6.5; 
            double radius = (1.0 - ti) * 75.0; // Starts at 75px radius, narrows to 0
            
            double x = lerpX + radius * Math.Cos(angle);
            double y = lerpY + radius * Math.Sin(angle);

            // Particle specs
            double size = 16.0 * (1.0 - (t - ti) * 3.0);
            if (size <= 0.5) continue;

            double opacity = (1.0 - (t - ti) * 2.5) * (1.0 - t * 0.3);
            if (opacity <= 0.01) continue;

            var particle = new Ellipse
            {
                Width = size,
                Height = size,
                Opacity = opacity,
                Fill = new RadialGradientBrush
                {
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0),
                        new GradientStop(Color.FromArgb(180, accentColor.R, accentColor.G, accentColor.B), 0.25),
                        new GradientStop(Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B), 1.0)
                    }
                }
            };

            // Glow aura
            particle.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accentColor,
                BlurRadius = size * 0.8,
                ShadowDepth = 0,
                Opacity = opacity * 0.8
            };

            Canvas.SetLeft(particle, x - size / 2);
            Canvas.SetTop(particle, y - size / 2);
            
            ParticleCanvas.Children.Add(particle);
        }
    }
}
