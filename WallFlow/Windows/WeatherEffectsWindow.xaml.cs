using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WallFlow.Windows;

public partial class WeatherEffectsWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private extern static int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private extern static int SetWindowLong(IntPtr hwnd, int index, int dwNewLong);

    [DllImport("user32.dll")]
    private extern static IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private extern static IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private extern static bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private extern static IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private extern static IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point lppoint);

    private class Particle
    {
        public Shape Visual { get; set; } = null!;
        public double X { get; set; }
        public double Y { get; set; }
        public double Speed { get; set; }
        public double Size { get; set; }
        public double Opacity { get; set; }
        
        // For swaying (snow, bubbles, sakura, leaves, aurora)
        public double BaseX { get; set; }
        public double Amplitude { get; set; }
        public double SpeedX { get; set; }
        public double TargetAngle { get; set; }
        
        // For stars/fireflies/energy twinkling
        public double MaxOpacity { get; set; }
        public double TwinkleSpeed { get; set; }
        public bool TwinkleDirection { get; set; }

        // For rotation (sakura, leaves)
        public double Rotation { get; set; }
        public double RotationSpeed { get; set; }

        // For fireflies movement direction
        public double DirectionAngle { get; set; }
        
        // Velocity (for energy particles)
        public double SpeedXVelocity { get; set; }
        public double SpeedYVelocity { get; set; }
    }

    private readonly List<Particle> _particles = new();
    private readonly Random _random = new();
    private string _currentEffect = "Ninguno";
    private bool _isRendering;
    private double _canvasWidth;
    private double _canvasHeight;
    private Rectangle? _lightningOverlay;

    public WeatherEffectsWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            
            // Hide from Alt+Tab and make it completely click-through
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            // Parent to Windows Desktop (WorkerW) behind icons
            ParentToDesktop(this);
        }
        catch { }

        // Start rendering loop
        _canvasWidth = SystemParameters.PrimaryScreenWidth;
        _canvasHeight = SystemParameters.PrimaryScreenHeight;
        Width = _canvasWidth;
        Height = _canvasHeight;
        Left = 0;
        Top = 0;

        if (_currentEffect != "Ninguno")
        {
            StartRendering();
        }
    }

    private static void ParentToDesktop(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        IntPtr progman = FindWindow("Progman", null);
        
        // Spawn WorkerW
        SendMessage(progman, 0x052C, new IntPtr(0), IntPtr.Zero);
        
        IntPtr workerw = IntPtr.Zero;
        EnumWindows(new EnumWindowsProc((tophandle, topparamhandle) =>
        {
            IntPtr p = FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", "");
            if (p != IntPtr.Zero)
            {
                workerw = FindWindowEx(IntPtr.Zero, tophandle, "WorkerW", "");
            }
            return true;
        }), IntPtr.Zero);
        
        if (workerw != IntPtr.Zero)
        {
            SetParent(hwnd, workerw);
        }
    }

    public void SetEffect(string effectName)
    {
        string newEffect = effectName ?? "Ninguno";
        if (newEffect != _currentEffect)
        {
            _currentEffect = newEffect;
            if (newEffect == "Ninguno")
            {
                StopRendering();
                ParticleCanvas.Children.Clear();
                _particles.Clear();
                _lightningOverlay = null;
            }
            else
            {
                if (!_isRendering)
                {
                    StartRendering();
                }
                else
                {
                    InitParticles();
                }
            }
        }
    }

    public void StartRendering()
    {
        if (_isRendering) return;
        _isRendering = true;
        CompositionTarget.Rendering += OnRendering;
        InitParticles();
    }

    public void StopRendering()
    {
        if (!_isRendering) return;
        _isRendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopRendering();
        base.OnClosed(e);
    }

    private void InitParticles()
    {
        // Clear old visual particles
        ParticleCanvas.Children.Clear();
        _particles.Clear();
        _lightningOverlay = null;

        int maxParticles = _currentEffect switch
        {
            "Lluvia" => 120,
            "Nieve" => 80,
            "Estrellas" => 60,
            "Hojas de Cerezo" => 40,
            "Luciérnagas" => 35,
            "Burbujas" => 30,
            "Aurora Boreal" => 8,
            "Hojas de Otoño" => 40,
            "Partículas de Energía" => 50,
            "Tormenta Eléctrica" => 160,
            _ => 0
        };

        // Apply density settings
        double densityMult = Services.SettingsService.Instance.WeatherEffectDensity / 100.0;
        maxParticles = (int)(maxParticles * densityMult);

        if (_currentEffect == "Tormenta Eléctrica" && maxParticles > 0)
        {
            _lightningOverlay = new Rectangle
            {
                Width = _canvasWidth,
                Height = _canvasHeight,
                Fill = Brushes.White,
                Opacity = 0
            };
            ParticleCanvas.Children.Add(_lightningOverlay);
        }

        for (int i = 0; i < maxParticles; i++)
        {
            var p = CreateParticle();
            _particles.Add(p);
            ParticleCanvas.Children.Add(p.Visual);
        }
    }

    private SolidColorBrush GetFrozenBrush(string hexColor)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            var brush = new SolidColorBrush(Colors.White);
            brush.Freeze();
            return brush;
        }
    }

    private Brush GetFrozenGradientBrush(string hexColor1, string hexColor2, Point startPoint, Point endPoint)
    {
        try
        {
            var color1 = (Color)ColorConverter.ConvertFromString(hexColor1);
            var color2 = (Color)ColorConverter.ConvertFromString(hexColor2);
            var brush = new LinearGradientBrush(color1, color2, startPoint, endPoint);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return GetFrozenBrush(hexColor1);
        }
    }

    private Particle CreateParticle(bool startAtTop = false)
    {
        var p = new Particle();
        
        if (_currentEffect == "Lluvia" || _currentEffect == "Tormenta Eléctrica")
        {
            p.Visual = new Line();
        }
        else if (_currentEffect == "Nieve" || _currentEffect == "Estrellas" || _currentEffect == "Luciérnagas" || _currentEffect == "Burbujas" || _currentEffect == "Partículas de Energía" || _currentEffect == "Hojas de Cerezo" || _currentEffect == "Hojas de Otoño")
        {
            var ell = new Ellipse();
            if (_currentEffect == "Hojas de Cerezo" || _currentEffect == "Hojas de Otoño")
            {
                ell.RenderTransformOrigin = new Point(0.5, 0.5);
                ell.RenderTransform = new RotateTransform(0);
            }
            else if (_currentEffect == "Luciérnagas")
            {
                var glow = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString(Services.SettingsService.Instance.EffectColor_Fireflies),
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
                ell.Effect = glow;
            }
            p.Visual = ell;
        }
        else if (_currentEffect == "Aurora Boreal")
        {
            p.Visual = new Rectangle();
        }
        else
        {
            p.Visual = new Ellipse();
        }

        ResetParticle(p, startAtTop);
        return p;
    }

    private void ResetParticle(Particle p, bool startAtTop)
    {
        p.X = _random.NextDouble() * _canvasWidth;
        double sizeMult = Services.SettingsService.Instance.WeatherEffectSize;
        var s = Services.SettingsService.Instance;

        if (_currentEffect == "Lluvia" || _currentEffect == "Tormenta Eléctrica")
        {
            bool isStorm = _currentEffect == "Tormenta Eléctrica";
            p.Y = startAtTop ? -20 : _random.NextDouble() * _canvasHeight;
            p.Speed = (isStorm ? 22 : 16) + _random.NextDouble() * 12;
            p.Size = (1 + _random.NextDouble() * 1.5) * sizeMult;
            p.Opacity = 0.15 + _random.NextDouble() * 0.25;

            if (p.Visual is Line line)
            {
                var hex1 = isStorm ? s.EffectColor_Thunderstorm : s.EffectColor_Rain;
                var hex2 = isStorm ? s.EffectColor_Thunderstorm2 : s.EffectColor_Rain2;
                line.Stroke = GetFrozenGradientBrush(hex1, hex2, new Point(0, 0), new Point(0, 1));
                line.StrokeThickness = p.Size;
                line.X1 = 0;
                line.Y1 = 0;
                line.X2 = -2;
                line.Y2 = (isStorm ? (24 + _random.NextDouble() * 12) : (18 + _random.NextDouble() * 10)) * sizeMult;
                line.Opacity = p.Opacity;
            }
        }
        else if (_currentEffect == "Nieve")
        {
            p.Y = startAtTop ? -20 : _random.NextDouble() * _canvasHeight;
            p.Speed = 1.2 + _random.NextDouble() * 2.0;
            p.Size = (2.0 + _random.NextDouble() * 4.0) * sizeMult;
            p.Opacity = 0.3 + _random.NextDouble() * 0.5;
            p.BaseX = p.X;
            p.Amplitude = 5 + _random.NextDouble() * 15;
            p.SpeedX = _random.NextDouble() * 0.05;
            p.TargetAngle = _random.NextDouble() * Math.PI * 2;

            if (p.Visual is Ellipse ell)
            {
                ell.Width = p.Size;
                ell.Height = p.Size;
                ell.Fill = GetFrozenGradientBrush(s.EffectColor_Snow, s.EffectColor_Snow2, new Point(0, 0), new Point(0, 1));
                ell.Opacity = p.Opacity;
            }
        }
        else if (_currentEffect == "Hojas de Cerezo" || _currentEffect == "Hojas de Otoño")
        {
            bool isCerezo = _currentEffect == "Hojas de Cerezo";
            p.Y = startAtTop ? -20 : _random.NextDouble() * _canvasHeight;
            p.Speed = 1.0 + _random.NextDouble() * 1.8;
            p.Size = (8.0 + _random.NextDouble() * 8.0) * sizeMult;
            p.Opacity = 0.4 + _random.NextDouble() * 0.5;
            p.BaseX = p.X;
            p.Amplitude = 10 + _random.NextDouble() * 20;
            p.SpeedX = 0.01 + _random.NextDouble() * 0.03;
            p.TargetAngle = _random.NextDouble() * Math.PI * 2;
            p.Rotation = _random.Next(360);
            p.RotationSpeed = 1.0 + _random.NextDouble() * 2.5;

            if (p.Visual is Ellipse ell)
            {
                ell.Width = p.Size;
                ell.Height = p.Size * 0.55;
                
                var hex1 = isCerezo ? s.EffectColor_Sakura : s.EffectColor_Leaves;
                var hex2 = isCerezo ? s.EffectColor_Sakura2 : s.EffectColor_Leaves2;
                ell.Fill = GetFrozenGradientBrush(hex1, hex2, new Point(0, 0), new Point(0, 1));
                ell.Opacity = p.Opacity;
                if (ell.RenderTransform is RotateTransform rot)
                {
                    rot.Angle = p.Rotation;
                }
            }
        }
        else if (_currentEffect == "Burbujas")
        {
            p.Y = startAtTop ? _canvasHeight + 20 : _random.NextDouble() * _canvasHeight;
            p.Speed = 1.0 + _random.NextDouble() * 1.5;
            p.Size = (6.0 + _random.NextDouble() * 14.0) * sizeMult;
            p.Opacity = 0.2 + _random.NextDouble() * 0.4;
            p.BaseX = p.X;
            p.Amplitude = 8 + _random.NextDouble() * 12;
            p.SpeedX = 0.02 + _random.NextDouble() * 0.03;
            p.TargetAngle = _random.NextDouble() * Math.PI * 2;

            if (p.Visual is Ellipse ell)
            {
                ell.Width = p.Size;
                ell.Height = p.Size;
                ell.Stroke = GetFrozenGradientBrush(s.EffectColor_Fog, s.EffectColor_Fog2, new Point(0, 0), new Point(1, 1)); 
                ell.StrokeThickness = 1;
                var fillBrush = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
                fillBrush.Freeze();
                ell.Fill = fillBrush;
                ell.Opacity = p.Opacity;
            }
        }
        else if (_currentEffect == "Estrellas")
        {
            p.Y = _random.NextDouble() * _canvasHeight;
            p.Size = (1.0 + _random.NextDouble() * 2.2) * sizeMult;
            p.MaxOpacity = 0.15 + _random.NextDouble() * 0.65;
            p.Opacity = _random.NextDouble() * p.MaxOpacity;
            p.TwinkleSpeed = 0.005 + _random.NextDouble() * 0.015;
            p.TwinkleDirection = _random.Next(2) == 0;

            if (p.Visual is Ellipse ell)
            {
                ell.Width = p.Size;
                ell.Height = p.Size;
                ell.Fill = GetFrozenGradientBrush(s.EffectColor_Stars, s.EffectColor_Stars2, new Point(0, 0), new Point(1, 1));
                ell.Opacity = p.Opacity;
            }
        }
        else if (_currentEffect == "Luciérnagas")
        {
            p.Y = _random.NextDouble() * _canvasHeight;
            p.Speed = 0.4 + _random.NextDouble() * 0.6;
            p.Size = (4.0 + _random.NextDouble() * 4.5) * sizeMult;
            p.MaxOpacity = 0.3 + _random.NextDouble() * 0.6;
            p.Opacity = _random.NextDouble() * p.MaxOpacity;
            p.TwinkleSpeed = 0.005 + _random.NextDouble() * 0.01;
            p.TwinkleDirection = _random.Next(2) == 0;
            p.DirectionAngle = _random.NextDouble() * Math.PI * 2;
            p.SpeedX = 0.02 + _random.NextDouble() * 0.05;

            if (p.Visual is Ellipse ell)
            {
                ell.Width = p.Size;
                ell.Height = p.Size;
                ell.Fill = GetFrozenGradientBrush(s.EffectColor_Fireflies, s.EffectColor_Fireflies2, new Point(0, 0), new Point(1, 1));
                ell.Opacity = p.Opacity;
            }
        }
        else if (_currentEffect == "Aurora Boreal")
        {
            p.Y = -50;
            p.Speed = 0.01 + _random.NextDouble() * 0.02;
            p.Size = (2.0 + _random.NextDouble() * 3.0) * sizeMult; 
            p.BaseX = p.X;
            p.Amplitude = 40 + _random.NextDouble() * 80;
            p.SpeedX = 0.001 + _random.NextDouble() * 0.003;
            p.TargetAngle = _random.NextDouble() * Math.PI * 2;
            p.MaxOpacity = 0.05 + _random.NextDouble() * 0.15;
            p.Opacity = p.MaxOpacity;

            if (p.Visual is Rectangle rect)
            {
                rect.Width = p.Size * 150;
                rect.Height = _canvasHeight * 1.3;
                
                var color1 = (Color)ColorConverter.ConvertFromString(s.EffectColor_Aurora);
                var color2 = (Color)ColorConverter.ConvertFromString(s.EffectColor_Aurora2 ?? "#660000FF");
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0)
                };
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, color1.R, color1.G, color1.B), 0));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * 0.45), color1.R, color1.G, color1.B), 0.35));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * 0.45), color2.R, color2.G, color2.B), 0.65));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, color2.R, color2.G, color2.B), 1));
                grad.Freeze();

                rect.Fill = grad;
                rect.Opacity = p.Opacity;
            }
        }
        else if (_currentEffect == "Partículas de Energía")
        {
            p.Y = _random.NextDouble() * _canvasHeight;
            p.SpeedXVelocity = (_random.NextDouble() - 0.5) * 2;
            p.SpeedYVelocity = (_random.NextDouble() - 0.5) * 2;
            p.Size = (3.0 + _random.NextDouble() * 4.0) * sizeMult;
            p.MaxOpacity = 0.4 + _random.NextDouble() * 0.5;
            p.Opacity = _random.NextDouble() * p.MaxOpacity;
            p.TwinkleSpeed = 0.01 + _random.NextDouble() * 0.02;
            p.TwinkleDirection = _random.Next(2) == 0;

            if (p.Visual is Ellipse ell)
            {
                ell.Width = p.Size;
                ell.Height = p.Size;
                ell.Fill = GetFrozenGradientBrush(s.EffectColor_Energy, s.EffectColor_Energy2, new Point(0, 0), new Point(1, 1));
                ell.Opacity = p.Opacity;
            }
        }

        Canvas.SetLeft(p.Visual, p.X);
        Canvas.SetTop(p.Visual, p.Y);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_canvasWidth == 0 || _canvasHeight == 0) return;

        double speedMult = Services.SettingsService.Instance.WeatherEffectSpeed;
        bool interactMouse = Services.SettingsService.Instance.WeatherEffectMouseInteraction;
        int mouseX = 0;
        int mouseY = 0;

        if (interactMouse)
        {
            if (GetCursorPos(out var win32Point))
            {
                mouseX = win32Point.X;
                mouseY = win32Point.Y;
            }
            else
            {
                interactMouse = false;
            }
        }

        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];

            if (_currentEffect == "Lluvia" || _currentEffect == "Tormenta Eléctrica")
            {
                bool isStorm = _currentEffect == "Tormenta Eléctrica";
                p.Y += p.Speed * speedMult;
                p.X -= (isStorm ? 2.2 : 1.8) * speedMult; // wind

                if (interactMouse)
                {
                    double dx = p.X - mouseX;
                    double dy = p.Y - mouseY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 90 && dist > 0)
                    {
                        double force = (90 - dist) / 90;
                        p.X += (dx / dist) * force * 15;
                        p.Y += (dy / dist) * force * 15;
                    }
                }

                if (p.Y > _canvasHeight || p.X < -20)
                {
                    ResetParticle(p, true);
                }
                else
                {
                    Canvas.SetLeft(p.Visual, p.X);
                    Canvas.SetTop(p.Visual, p.Y);
                }
            }
            else if (_currentEffect == "Nieve")
            {
                p.Y += p.Speed * speedMult;
                p.TargetAngle += p.SpeedX * speedMult;
                p.X = p.BaseX + Math.Sin(p.TargetAngle) * p.Amplitude;

                if (interactMouse)
                {
                    double dx = p.X - mouseX;
                    double dy = p.Y - mouseY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 90 && dist > 0)
                    {
                        double force = (90 - dist) / 90;
                        p.X += (dx / dist) * force * 10;
                        p.Y += (dy / dist) * force * 10;
                    }
                }

                if (p.Y > _canvasHeight)
                {
                    ResetParticle(p, true);
                }
                else
                {
                    Canvas.SetLeft(p.Visual, p.X);
                    Canvas.SetTop(p.Visual, p.Y);
                }
            }
            else if (_currentEffect == "Hojas de Cerezo" || _currentEffect == "Hojas de Otoño")
            {
                p.Y += p.Speed * speedMult;
                p.TargetAngle += p.SpeedX * speedMult;
                p.X = p.BaseX + Math.Sin(p.TargetAngle) * p.Amplitude;

                p.Rotation += p.RotationSpeed * speedMult;
                if (p.Visual.RenderTransform is RotateTransform rot)
                {
                    rot.Angle = p.Rotation;
                }

                if (interactMouse)
                {
                    double dx = p.X - mouseX;
                    double dy = p.Y - mouseY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 95 && dist > 0)
                    {
                        double force = (95 - dist) / 95;
                        p.X += (dx / dist) * force * 12;
                        p.Y += (dy / dist) * force * 12;
                    }
                }

                if (p.Y > _canvasHeight)
                {
                    ResetParticle(p, true);
                }
                else
                {
                    Canvas.SetLeft(p.Visual, p.X);
                    Canvas.SetTop(p.Visual, p.Y);
                }
            }
            else if (_currentEffect == "Burbujas")
            {
                p.Y -= p.Speed * speedMult; // rise up
                p.TargetAngle += p.SpeedX * speedMult;
                p.X = p.BaseX + Math.Sin(p.TargetAngle) * p.Amplitude;

                bool popped = false;
                if (interactMouse)
                {
                    double dx = p.X - mouseX;
                    double dy = p.Y - mouseY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 20)
                    {
                        // Pop bubble! Reset to bottom
                        ResetParticle(p, true);
                        popped = true;
                    }
                    else if (dist < 90 && dist > 0)
                    {
                        double force = (90 - dist) / 90;
                        p.X += (dx / dist) * force * 8;
                        p.Y += (dy / dist) * force * 8;
                    }
                }

                if (!popped)
                {
                    if (p.Y < -50)
                    {
                        ResetParticle(p, true);
                    }
                    else
                    {
                        Canvas.SetLeft(p.Visual, p.X);
                        Canvas.SetTop(p.Visual, p.Y);
                    }
                }
            }
            else if (_currentEffect == "Estrellas")
            {
                // Twinkle
                if (p.TwinkleDirection)
                {
                    p.Opacity += p.TwinkleSpeed * speedMult;
                    if (p.Opacity >= p.MaxOpacity)
                    {
                        p.Opacity = p.MaxOpacity;
                        p.TwinkleDirection = false;
                    }
                }
                else
                {
                    p.Opacity -= p.TwinkleSpeed * speedMult;
                    if (p.Opacity <= 0.02)
                    {
                        p.Opacity = 0.02;
                        p.TwinkleDirection = true;
                    }
                }
                p.Visual.Opacity = p.Opacity;
            }
            else if (_currentEffect == "Luciérnagas")
            {
                // Drift randomly
                p.DirectionAngle += (_random.NextDouble() - 0.5) * p.SpeedX * speedMult;

                if (interactMouse)
                {
                    double dx = mouseX - p.X;
                    double dy = mouseY - p.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 280 && dist > 0)
                    {
                        // Attract to mouse
                        double targetAngle = Math.Atan2(dy, dx);
                        p.DirectionAngle += (targetAngle - p.DirectionAngle) * 0.05 * speedMult;
                    }
                }

                p.X += Math.Cos(p.DirectionAngle) * p.Speed * speedMult;
                p.Y += Math.Sin(p.DirectionAngle) * p.Speed * speedMult;

                // Twinkle
                if (p.TwinkleDirection)
                {
                    p.Opacity += p.TwinkleSpeed * speedMult;
                    if (p.Opacity >= p.MaxOpacity)
                    {
                        p.Opacity = p.MaxOpacity;
                        p.TwinkleDirection = false;
                    }
                }
                else
                {
                    p.Opacity -= p.TwinkleSpeed * speedMult;
                    if (p.Opacity <= 0.05)
                    {
                        p.Opacity = 0.05;
                        p.TwinkleDirection = true;
                    }
                }
                p.Visual.Opacity = p.Opacity;

                // Reset if floats off screen
                if (p.X < -50 || p.X > _canvasWidth + 50 || p.Y < -50 || p.Y > _canvasHeight + 50)
                {
                    ResetParticle(p, false);
                    p.DirectionAngle = _random.NextDouble() * Math.PI * 2;
                }
                else
                {
                    Canvas.SetLeft(p.Visual, p.X);
                    Canvas.SetTop(p.Visual, p.Y);
                }
            }
            else if (_currentEffect == "Aurora Boreal")
            {
                p.TargetAngle += p.SpeedX * speedMult;
                p.X = p.BaseX + Math.Sin(p.TargetAngle) * p.Amplitude;
                p.Opacity = (Math.Sin(p.TargetAngle * 0.5) + 1.0) * 0.5 * p.MaxOpacity;
                p.Visual.Opacity = p.Opacity;

                Canvas.SetLeft(p.Visual, p.X);
                Canvas.SetTop(p.Visual, p.Y);
            }
            else if (_currentEffect == "Partículas de Energía")
            {
                if (interactMouse)
                {
                    double dx = mouseX - p.X;
                    double dy = mouseY - p.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 400 && dist > 0)
                    {
                        p.SpeedXVelocity += (dx / dist) * 0.15 * speedMult;
                        p.SpeedYVelocity += (dy / dist) * 0.15 * speedMult;
                    }
                    else
                    {
                        p.SpeedXVelocity += (_random.NextDouble() - 0.5) * 0.2 * speedMult;
                        p.SpeedYVelocity += (_random.NextDouble() - 0.5) * 0.2 * speedMult;
                    }
                }
                else
                {
                    p.SpeedXVelocity += (_random.NextDouble() - 0.5) * 0.2 * speedMult;
                    p.SpeedYVelocity += (_random.NextDouble() - 0.5) * 0.2 * speedMult;
                }

                // Limit speed
                double speed = Math.Sqrt(p.SpeedXVelocity * p.SpeedXVelocity + p.SpeedYVelocity * p.SpeedYVelocity);
                if (speed > 5)
                {
                    p.SpeedXVelocity = (p.SpeedXVelocity / speed) * 5;
                    p.SpeedYVelocity = (p.SpeedYVelocity / speed) * 5;
                }

                p.X += p.SpeedXVelocity * speedMult;
                p.Y += p.SpeedYVelocity * speedMult;

                // Twinkle
                if (p.TwinkleDirection)
                {
                    p.Opacity += p.TwinkleSpeed * speedMult;
                    if (p.Opacity >= p.MaxOpacity)
                    {
                        p.Opacity = p.MaxOpacity;
                        p.TwinkleDirection = false;
                    }
                }
                else
                {
                    p.Opacity -= p.TwinkleSpeed * speedMult;
                    if (p.Opacity <= 0.1)
                    {
                        p.Opacity = 0.1;
                        p.TwinkleDirection = true;
                    }
                }
                p.Visual.Opacity = p.Opacity;

                // Wrap around edges
                if (p.X < -20) p.X = _canvasWidth + 20;
                else if (p.X > _canvasWidth + 20) p.X = -20;
                
                if (p.Y < -20) p.Y = _canvasHeight + 20;
                else if (p.Y > _canvasHeight + 20) p.Y = -20;

                Canvas.SetLeft(p.Visual, p.X);
                Canvas.SetTop(p.Visual, p.Y);
            }
        }

        if (_currentEffect == "Tormenta Eléctrica" && _lightningOverlay != null)
        {
            if (_random.NextDouble() < 0.005)
            {
                _lightningOverlay.Opacity = 0.2 + _random.NextDouble() * 0.4;
            }
            else if (_lightningOverlay.Opacity > 0)
            {
                _lightningOverlay.Opacity -= 0.05 * speedMult;
                if (_lightningOverlay.Opacity < 0) _lightningOverlay.Opacity = 0;
            }
        }
    }
}
