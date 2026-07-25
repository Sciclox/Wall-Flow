using System;
using System.Windows;
using System.Windows.Media;
using WallFlow.Services;

namespace WallFlow.Windows
{
    public partial class WeatherWindow : Window, IWidget
    {
        string IWidget.Name => "Weather";
        public void Open() { Show(); Activate(); }
        public bool IsOpen => IsLoaded && IsVisible;
        public static WeatherWindow? Instance { get; private set; }

        private readonly double _targetLeft;

        public WeatherWindow(double left)
        {
            InitializeComponent();
            Instance = this;
            _targetLeft = left;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Apply acrylic blur
            BackdropHelper.ApplyAcrylic(this);
            BackdropHelper.UpdateWindowRegion(this, 8);

            // Positioning relative to target screen
            var screens = System.Windows.Forms.Screen.AllScreens;
            var screenIndex = Math.Clamp(SettingsService.Instance.DockMonitorIndex, 0, screens.Length - 1);
            var screen = screens[screenIndex];
            var dpi = VisualTreeHelper.GetDpi(this);

            double screenLeft = screen.Bounds.X / dpi.DpiScaleX;
            double screenRight = (screen.Bounds.X + screen.Bounds.Width) / dpi.DpiScaleX;

            Left = _targetLeft;
            bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
            double finalTop;
            if (isBottom)
            {
                finalTop = DockWindow.Instance != null 
                    ? DockWindow.Instance.Top - Height 
                    : (screen.Bounds.Y + screen.Bounds.Height) / dpi.DpiScaleY - Height - 56;
            }
            else
            {
                finalTop = DockWindow.Instance != null 
                    ? DockWindow.Instance.Top + DockWindow.Instance.ActualHeight 
                    : (screen.Bounds.Y / dpi.DpiScaleY) + 56;
            }
            
            Top = isBottom ? finalTop + 15 : finalTop - 15;
            Opacity = 0;

            // Ensure window does not go off-screen on the target monitor
            if (Left < screenLeft) Left = screenLeft + 10;
            if (Left + Width > screenRight)
            {
                Left = screenRight - Width - 10;
            }

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) 
            { 
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } 
            };
            var slide = new System.Windows.Media.Animation.DoubleAnimation(isBottom ? finalTop + 15 : finalTop - 15, finalTop, TimeSpan.FromMilliseconds(250)) 
            { 
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } 
            };

            BeginAnimation(OpacityProperty, fadeIn);
            BeginAnimation(TopProperty, slide);

            // Load weather data
            await LoadWeatherDataAsync();
        }

        private async System.Threading.Tasks.Task LoadWeatherDataAsync()
        {
            try
            {
                var weather = await WeatherService.Instance.GetWeatherAsync();

                if (WeatherService.TryParseCoordinates(weather.City, out _, out _))
                {
                    CityText.Text = "Mi Ubicación";
                }
                else
                {
                    CityText.Text = weather.City;
                }

                TempText.Text = $" {Math.Round(weather.Temperature)}°C";
                ConditionText.Text = $"Sensación {Math.Round(weather.ApparentTemperature)}°C  •  {weather.Condition}  •  Humedad {weather.Humidity}%";
                StatsText.Text = $"Viento {Math.Round(weather.WindSpeed)} km/h  •  Presión {Math.Round(weather.Pressure)} hPa  •  Nubosidad {weather.CloudCover}%";

                ForecastItems.ItemsSource = weather.Forecast;
            }
            catch
            {
                CityText.Text = "Error";
                ConditionText.Text = "No se pudo cargar el clima";
                StatsText.Text = "";
            }
        }

        private void EditCityButton_Click(object sender, RoutedEventArgs e)
        {
            CityHeaderPanel.Visibility = Visibility.Collapsed;
            CitySearchBox.Visibility = Visibility.Visible;
            CitySearchBox.Text = CityText.Text == "Sin Conexión" || CityText.Text == "Error" ? "" : CityText.Text;
            CitySearchBox.Focus();
            CitySearchBox.SelectAll();
        }

        private async void CitySearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                string input = CitySearchBox.Text.Trim();
                if (WeatherService.TryParseCoordinates(input, out double lat, out double lon))
                {
                    SettingsService.Instance.WeatherCity = $"{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                }
                else
                {
                    SettingsService.Instance.WeatherCity = input;
                }
                SettingsService.Save();

                WeatherService.Instance.ResetCache();

                CitySearchBox.Visibility = Visibility.Collapsed;
                CityHeaderPanel.Visibility = Visibility.Visible;

                ConditionText.Text = "Cargando...";
                StatsText.Text = "";
                await LoadWeatherDataAsync();

                if (DockWindow.Instance != null)
                {
                    DockWindow.Instance.UpdateWeather();
                }
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                CitySearchBox.Visibility = Visibility.Collapsed;
                CityHeaderPanel.Visibility = Visibility.Visible;
            }
        }

        private void CitySearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CitySearchBox.Visibility = Visibility.Collapsed;
            CityHeaderPanel.Visibility = Visibility.Visible;
        }

        private bool _isClosing = false;

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (_isClosing) return;
            // Close unless search box is focused
            if (!CitySearchBox.IsFocused)
            {
                SafeClose();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            base.OnClosing(e);
        }

        private void SafeClose()
        {
            if (_isClosing) return;
            _isClosing = true;

            var screens = System.Windows.Forms.Screen.AllScreens;
            var screenIndex = Math.Clamp(SettingsService.Instance.DockMonitorIndex, 0, screens.Length - 1);
            var screen = screens[screenIndex];
            var dpi = VisualTreeHelper.GetDpi(this);

            bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
            double finalTop;
            if (isBottom)
            {
                finalTop = DockWindow.Instance != null 
                    ? DockWindow.Instance.Top - Height 
                    : (screen.Bounds.Y + screen.Bounds.Height) / dpi.DpiScaleY - Height - 56;
            }
            else
            {
                finalTop = DockWindow.Instance != null 
                    ? DockWindow.Instance.Top + DockWindow.Instance.ActualHeight 
                    : (screen.Bounds.Y / dpi.DpiScaleY) + 56;
            }

            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) 
            { 
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } 
            };
            var slide = new System.Windows.Media.Animation.DoubleAnimation(isBottom ? finalTop + 15 : finalTop - 15, TimeSpan.FromMilliseconds(200)) 
            { 
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } 
            };

            fadeOut.Completed += (snd, args) =>
            {
                Close();
            };

            BeginAnimation(OpacityProperty, fadeOut);
            BeginAnimation(TopProperty, slide);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (Instance == this)
            {
                Instance = null;
            }

            // Force GC collection to immediately free resources of the weather window
            GC.Collect();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
        }
    }
}
