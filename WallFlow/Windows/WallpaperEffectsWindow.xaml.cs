using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WallFlow.Native;
using static WallFlow.Native.NativeMethods;

namespace WallFlow.Windows;

public partial class WallpaperEffectsWindow : Window
{
    private class Particle
    {
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

    private class ParticleSurface : FrameworkElement
    {
        private readonly WallpaperEffectsWindow _window;

        public ParticleSurface(WallpaperEffectsWindow window)
        {
            _window = window;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            _window.RenderParticles(dc);
        }
    }

    private readonly List<Particle> _particles = new();
    private readonly Random _random = new();
    private string _currentEffect = "Ninguno";
    private bool _isRendering;
    private double _canvasWidth;
    private double _canvasHeight;
    private double _lightningAlpha = 0;
    private DispatcherTimer? _zOrderTimer;
    private ParticleSurface? _surface;

    // Cached frozen brushes and pens
    private Brush? _cachedBrush1;
    private Brush? _cachedBrush2;
    private Pen? _cachedPen;

    public WallpaperEffectsWindow()
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

            // Push to bottom of Z-order immediately
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }

        // Start rendering loop (covers full virtual screen across multi-monitors)
        _canvasWidth = SystemParameters.VirtualScreenWidth > 0 ? SystemParameters.VirtualScreenWidth : SystemParameters.PrimaryScreenWidth;
        _canvasHeight = SystemParameters.VirtualScreenHeight > 0 ? SystemParameters.VirtualScreenHeight : SystemParameters.PrimaryScreenHeight;
        Width = _canvasWidth;
        Height = _canvasHeight;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;

        _surface = new ParticleSurface(this)
        {
            Width = _canvasWidth,
            Height = _canvasHeight
        };
        RootGrid.Children.Add(_surface);

        // Safety timer: keep window at bottom of Z-order every 2 seconds
        _zOrderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _zOrderTimer.Tick += (s, args) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }
        };

        if (_currentEffect != "Ninguno")
        {
            StartRendering();
        }
        else
        {
            _zOrderTimer.Stop();
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
        catch { }
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
                _particles.Clear();
                _lightningAlpha = 0;
                _surface?.InvalidateVisual();
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
        if (_zOrderTimer != null && !_zOrderTimer.IsEnabled)
        {
            _zOrderTimer.Start();
        }
        InitParticles();
    }

    public void StopRendering()
    {
        _isRendering = false;
        CompositionTarget.Rendering -= OnRendering;
        if (_zOrderTimer != null && _zOrderTimer.IsEnabled)
        {
            _zOrderTimer.Stop();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopRendering();
        if (_zOrderTimer != null)
        {
            _zOrderTimer.Stop();
            _zOrderTimer = null;
        }
        base.OnClosed(e);
    }

    private void InitParticles()
    {
        _particles.Clear();
        _lightningAlpha = 0;

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

        double densityMult = Services.SettingsService.Instance.WeatherEffectDensity / 100.0;
        maxParticles = (int)(maxParticles * densityMult);

        UpdateCachedBrushes();

        for (int i = 0; i < maxParticles; i++)
        {
            var p = new Particle();
            ResetParticle(p, false);
            _particles.Add(p);
        }

        _surface?.InvalidateVisual();
    }

    private void UpdateCachedBrushes()
    {
        var s = Services.SettingsService.Instance;
        switch (_currentEffect)
        {
            case "Lluvia":
                _cachedBrush1 = GetFrozenGradientBrush(s.EffectColor_Rain, s.EffectColor_Rain2, new Point(0, 0), new Point(0, 1));
                _cachedPen = new Pen(_cachedBrush1, 1.5);
                _cachedPen.Freeze();
                break;
            case "Tormenta Eléctrica":
                _cachedBrush1 = GetFrozenGradientBrush(s.EffectColor_Thunderstorm, s.EffectColor_Thunderstorm2, new Point(0, 0), new Point(0, 1));
                _cachedPen = new Pen(_cachedBrush1, 2.0);
                _cachedPen.Freeze();
                break;
            case "Nieve":
                _cachedBrush1 = GetFrozenGradientBrush(s.EffectColor_Snow, s.EffectColor_Snow2, new Point(0, 0), new Point(0, 1));
                break;
            case "Hojas de Cerezo":
                _cachedBrush1 = GetFrozenGradientBrush(s.EffectColor_Sakura, s.EffectColor_Sakura2, new Point(0, 0), new Point(0, 1));
                break;
            case "Hojas de Otoño":
                _cachedBrush1 = GetFrozenGradientBrush(s.EffectColor_Leaves, s.EffectColor_Leaves2, new Point(0, 0), new Point(0, 1));
                break;
            case "Burbujas":
                _cachedBrush1 = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
                _cachedBrush1.Freeze();
                _cachedBrush2 = GetFrozenGradientBrush(s.EffectColor_Fog, s.EffectColor_Fog2, new Point(0, 0), new Point(1, 1));
                _cachedPen = new Pen(_cachedBrush2, 1.0);
                _cachedPen.Freeze();
                break;
            case "Estrellas":
                _cachedBrush1 = GetFrozenGradientBrush(s.EffectColor_Stars, s.EffectColor_Stars2, new Point(0, 0), new Point(1, 1));
                break;
            case "Luciérnagas":
                _cachedBrush1 = GetFrozenBrush(s.EffectColor_Fireflies);
                _cachedBrush2 = GetFrozenBrush(s.EffectColor_Fireflies2);
                break;
            case "Aurora Boreal":
                _cachedBrush1 = GetFrozenGradientBrush(s.EffectColor_Aurora, s.EffectColor_Aurora2, new Point(0, 0), new Point(1, 1));
                break;
            case "Partículas de Energía":
                _cachedBrush1 = GetFrozenGradientBrush(s.EffectColor_Energy, s.EffectColor_Energy2, new Point(0, 0), new Point(1, 1));
                break;
            default:
                _cachedBrush1 = Brushes.White;
                _cachedBrush2 = null;
                _cachedPen = null;
                break;
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

    private void ResetParticle(Particle p, bool startAtTop)
    {
        p.X = _random.NextDouble() * _canvasWidth;
        double sizeMult = Services.SettingsService.Instance.WeatherEffectSize;

        if (_currentEffect == "Lluvia" || _currentEffect == "Tormenta Eléctrica")
        {
            bool isStorm = _currentEffect == "Tormenta Eléctrica";
            p.Y = startAtTop ? -20 : _random.NextDouble() * _canvasHeight;
            p.Speed = (isStorm ? 22 : 16) + _random.NextDouble() * 12;
            p.Size = (1 + _random.NextDouble() * 1.5) * sizeMult;
            p.Opacity = 0.15 + _random.NextDouble() * 0.25;
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
        }
        else if (_currentEffect == "Hojas de Cerezo" || _currentEffect == "Hojas de Otoño")
        {
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
        }
        else if (_currentEffect == "Estrellas")
        {
            p.Y = _random.NextDouble() * _canvasHeight;
            p.Size = (1.0 + _random.NextDouble() * 2.2) * sizeMult;
            p.MaxOpacity = 0.15 + _random.NextDouble() * 0.65;
            p.Opacity = _random.NextDouble() * p.MaxOpacity;
            p.TwinkleSpeed = 0.005 + _random.NextDouble() * 0.015;
            p.TwinkleDirection = _random.Next(2) == 0;
        }
        else if (_currentEffect == "Luciérnagas")
        {
            p.Y = _random.NextDouble() * _canvasHeight;
            p.Speed = 0.5 + _random.NextDouble() * 1.0;
            p.Size = (3.0 + _random.NextDouble() * 3.0) * sizeMult;
            p.MaxOpacity = 0.4 + _random.NextDouble() * 0.5;
            p.Opacity = _random.NextDouble() * p.MaxOpacity;
            p.TwinkleSpeed = 0.01 + _random.NextDouble() * 0.02;
            p.TwinkleDirection = _random.Next(2) == 0;
            p.SpeedX = 0.02 + _random.NextDouble() * 0.04;
            p.DirectionAngle = _random.NextDouble() * Math.PI * 2;
        }
        else if (_currentEffect == "Aurora Boreal")
        {
            p.Y = 0;
            p.Size = (10.0 + _random.NextDouble() * 20.0) * sizeMult;
            p.MaxOpacity = 0.15 + _random.NextDouble() * 0.25;
            p.Opacity = p.MaxOpacity;
            p.BaseX = p.X;
            p.Amplitude = 40 + _random.NextDouble() * 80;
            p.SpeedX = 0.005 + _random.NextDouble() * 0.01;
            p.TargetAngle = _random.NextDouble() * Math.PI * 2;
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
        }
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
                p.X -= (isStorm ? 2.2 : 1.8) * speedMult;

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
            }
            else if (_currentEffect == "Hojas de Cerezo" || _currentEffect == "Hojas de Otoño")
            {
                p.Y += p.Speed * speedMult;
                p.TargetAngle += p.SpeedX * speedMult;
                p.X = p.BaseX + Math.Sin(p.TargetAngle) * p.Amplitude;
                p.Rotation += p.RotationSpeed * speedMult;

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
            }
            else if (_currentEffect == "Burbujas")
            {
                p.Y -= p.Speed * speedMult;
                p.TargetAngle += p.SpeedX * speedMult;
                p.X = p.BaseX + Math.Sin(p.TargetAngle) * p.Amplitude;

                if (interactMouse)
                {
                    double dx = p.X - mouseX;
                    double dy = p.Y - mouseY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 100 && dist > 0)
                    {
                        double force = (100 - dist) / 100;
                        p.X += (dx / dist) * force * 14;
                        p.Y += (dy / dist) * force * 14;
                    }
                }

                if (p.Y < -30)
                {
                    ResetParticle(p, true);
                }
            }
            else if (_currentEffect == "Estrellas")
            {
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
            }
            else if (_currentEffect == "Luciérnagas")
            {
                p.DirectionAngle += (_random.NextDouble() - 0.5) * p.SpeedX * speedMult;

                if (interactMouse)
                {
                    double dx = mouseX - p.X;
                    double dy = mouseY - p.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 280 && dist > 0)
                    {
                        double targetAngle = Math.Atan2(dy, dx);
                        p.DirectionAngle += (targetAngle - p.DirectionAngle) * 0.05 * speedMult;
                    }
                }

                p.X += Math.Cos(p.DirectionAngle) * p.Speed * speedMult;
                p.Y += Math.Sin(p.DirectionAngle) * p.Speed * speedMult;

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

                if (p.X < -50 || p.X > _canvasWidth + 50 || p.Y < -50 || p.Y > _canvasHeight + 50)
                {
                    ResetParticle(p, false);
                    p.DirectionAngle = _random.NextDouble() * Math.PI * 2;
                }
            }
            else if (_currentEffect == "Aurora Boreal")
            {
                p.TargetAngle += p.SpeedX * speedMult;
                p.X = p.BaseX + Math.Sin(p.TargetAngle) * p.Amplitude;
                p.Opacity = (Math.Sin(p.TargetAngle * 0.5) + 1.0) * 0.5 * p.MaxOpacity;
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

                double speed = Math.Sqrt(p.SpeedXVelocity * p.SpeedXVelocity + p.SpeedYVelocity * p.SpeedYVelocity);
                if (speed > 5)
                {
                    p.SpeedXVelocity = (p.SpeedXVelocity / speed) * 5;
                    p.SpeedYVelocity = (p.SpeedYVelocity / speed) * 5;
                }

                p.X += p.SpeedXVelocity * speedMult;
                p.Y += p.SpeedYVelocity * speedMult;

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

                if (p.X < -20) p.X = _canvasWidth + 20;
                else if (p.X > _canvasWidth + 20) p.X = -20;

                if (p.Y < -20) p.Y = _canvasHeight + 20;
                else if (p.Y > _canvasHeight + 20) p.Y = -20;
            }
        }

        if (_currentEffect == "Tormenta Eléctrica")
        {
            if (_random.NextDouble() < 0.005)
            {
                _lightningAlpha = 0.2 + _random.NextDouble() * 0.4;
            }
            else if (_lightningAlpha > 0)
            {
                _lightningAlpha -= 0.05 * speedMult;
                if (_lightningAlpha < 0) _lightningAlpha = 0;
            }
        }

        _surface?.InvalidateVisual();
    }

    private void RenderParticles(DrawingContext dc)
    {
        if (_particles.Count == 0 && _lightningAlpha <= 0) return;

        double sizeMult = Services.SettingsService.Instance.WeatherEffectSize;

        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];

            switch (_currentEffect)
            {
                case "Lluvia":
                case "Tormenta Eléctrica":
                    if (_cachedPen != null)
                    {
                        double lineLen = (_currentEffect == "Tormenta Eléctrica" ? 28 : 20) * sizeMult;
                        dc.PushOpacity(p.Opacity);
                        dc.DrawLine(_cachedPen, new Point(p.X, p.Y), new Point(p.X - 2, p.Y + lineLen));
                        dc.Pop();
                    }
                    break;

                case "Nieve":
                    if (_cachedBrush1 != null)
                    {
                        dc.PushOpacity(p.Opacity);
                        dc.DrawEllipse(_cachedBrush1, null, new Point(p.X, p.Y), p.Size * 0.5, p.Size * 0.5);
                        dc.Pop();
                    }
                    break;

                case "Hojas de Cerezo":
                case "Hojas de Otoño":
                    if (_cachedBrush1 != null)
                    {
                        dc.PushOpacity(p.Opacity);
                        dc.PushTransform(new RotateTransform(p.Rotation, p.X, p.Y));
                        dc.DrawEllipse(_cachedBrush1, null, new Point(p.X, p.Y), p.Size * 0.5, p.Size * 0.28);
                        dc.Pop();
                        dc.Pop();
                    }
                    break;

                case "Burbujas":
                    if (_cachedBrush1 != null && _cachedPen != null)
                    {
                        dc.PushOpacity(p.Opacity);
                        dc.DrawEllipse(_cachedBrush1, _cachedPen, new Point(p.X, p.Y), p.Size * 0.5, p.Size * 0.5);
                        dc.Pop();
                    }
                    break;

                case "Estrellas":
                    if (_cachedBrush1 != null)
                    {
                        dc.PushOpacity(p.Opacity);
                        dc.DrawEllipse(_cachedBrush1, null, new Point(p.X, p.Y), p.Size * 0.5, p.Size * 0.5);
                        dc.Pop();
                    }
                    break;

                case "Luciérnagas":
                    if (_cachedBrush1 != null && _cachedBrush2 != null)
                    {
                        dc.PushOpacity(p.Opacity * 0.4);
                        dc.DrawEllipse(_cachedBrush2, null, new Point(p.X, p.Y), p.Size * 1.6, p.Size * 1.6);
                        dc.Pop();

                        dc.PushOpacity(p.Opacity);
                        dc.DrawEllipse(_cachedBrush1, null, new Point(p.X, p.Y), p.Size * 0.5, p.Size * 0.5);
                        dc.Pop();
                    }
                    break;

                case "Aurora Boreal":
                    if (_cachedBrush1 != null)
                    {
                        dc.PushOpacity(p.Opacity);
                        dc.DrawRectangle(_cachedBrush1, null, new Rect(p.X, 0, p.Size * 15, _canvasHeight));
                        dc.Pop();
                    }
                    break;

                case "Partículas de Energía":
                    if (_cachedBrush1 != null)
                    {
                        dc.PushOpacity(p.Opacity);
                        dc.DrawEllipse(_cachedBrush1, null, new Point(p.X, p.Y), p.Size * 0.5, p.Size * 0.5);
                        dc.Pop();
                    }
                    break;
            }
        }

        if (_lightningAlpha > 0)
        {
            var flashBrush = new SolidColorBrush(Color.FromArgb((byte)(_lightningAlpha * 255), 255, 255, 255));
            flashBrush.Freeze();
            dc.DrawRectangle(flashBrush, null, new Rect(0, 0, _canvasWidth, _canvasHeight));
        }
    }
}
