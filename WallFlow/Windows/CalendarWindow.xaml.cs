using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WallFlow.Services;

namespace WallFlow.Windows;

public partial class CalendarWindow : Window, IWidget
{
    string IWidget.Name => "Calendar";
    public void Open() { Show(); Activate(); }
    public bool IsOpen => IsLoaded && IsVisible;
    private DateTime _displayMonth;

    private readonly double _targetLeft;

    public CalendarWindow(double left)
    {
        InitializeComponent();
        _displayMonth = DateTime.Today;
        _targetLeft = left;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply acrylic blur
        BackdropHelper.ApplyAcrylic(this);
        BackdropHelper.UpdateWindowRegion(this, 0);

        // Position window directly below the Clock section of the dock
        if (DockWindow.Instance != null)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var screenIndex = Math.Clamp(SettingsService.Instance.DockMonitorIndex, 0, screens.Length - 1);
            var screen = screens[screenIndex];
            var dpi = VisualTreeHelper.GetDpi(this);

            double screenLeft = screen.Bounds.X / dpi.DpiScaleX;
            double screenRight = (screen.Bounds.X + screen.Bounds.Width) / dpi.DpiScaleX;

            Left = _targetLeft;

            // Ensure window does not go off-screen on the target monitor
            if (Left < screenLeft) Left = screenLeft + 10;
            if (Left + Width > screenRight)
            {
                Left = screenRight - Width - 10;
            }

            bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
            double finalTop = isBottom 
                ? DockWindow.Instance.Top - Height 
                : DockWindow.Instance.Top + DockWindow.Instance.ActualHeight;
            
            Top = isBottom ? finalTop + 15 : finalTop - 15;
            Opacity = 0;

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
        }
        else
        {
            Left = _targetLeft;
            bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
            Top = isBottom ? SystemParameters.PrimaryScreenHeight - Height - 56 : 56;
        }

        // Set today's date info on the left pane
        var today = DateTime.Today;
        var culture = new CultureInfo("es-ES");
        
        TodayDayNameText.Text = today.ToString("dddd", culture).ToUpper();
        TodayMonthText.Text = today.ToString("MMMM", culture).ToUpper();
        TodayDayNumberText.Text = today.ToString("dd");

        UpdateCalendar();
    }

    private void UpdateCalendar()
    {
        DaysGrid.Children.Clear();

        // Header Month and Year (uppercase in Spanish)
        var culture = new CultureInfo("es-ES");
        string monthName = _displayMonth.ToString("MMMM", culture).ToUpper();
        MonthYearText.Text = $"{monthName} {_displayMonth.Year}";

        // First day of the selected month
        var firstDayOfMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
        
        // Offset mapping to start week on Monday (0=Lunes, 6=Domingo)
        int dayOffset = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;

        // Days in the previous month to fill the starting empty cells
        var prevMonth = firstDayOfMonth.AddMonths(-1);
        int prevMonthDaysCount = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);

        // Add previous month's trailing days (semi-transparent gray text)
        for (int i = dayOffset - 1; i >= 0; i--)
        {
            int dayNum = prevMonthDaysCount - i;
            AddDayCell(dayNum.ToString(), false, false);
        }

        // Add current month's days
        int currentMonthDaysCount = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
        var today = DateTime.Today;

        for (int d = 1; d <= currentMonthDaysCount; d++)
        {
            bool isToday = _displayMonth.Year == today.Year &&
                           _displayMonth.Month == today.Month &&
                           d == today.Day;

            AddDayCell(d.ToString(), true, isToday);
        }

        // Add next month's starting days to complete the 42 cells (6 rows x 7 columns)
        int cellsAddedSoFar = dayOffset + currentMonthDaysCount;
        int remainingCells = 42 - cellsAddedSoFar;

        for (int d = 1; d <= remainingCells; d++)
        {
            AddDayCell(d.ToString(), false, false);
        }
    }

    private void AddDayCell(string dayText, bool isCurrentMonth, bool isToday)
    {
        var cellGrid = new Grid
        {
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isToday)
        {
            var accentColor = Color.FromRgb(0xAF, 0x52, 0xDE); // Fallback to purple
            if (Application.Current.TryFindResource("AccentColor") is Color resolvedColor)
            {
                accentColor = resolvedColor;
            }

            // Circular dynamic background for today's date
            var circleBorder = new Border
            {
                Width = 28,
                Height = 28,
                Background = new SolidColorBrush(accentColor),
                CornerRadius = new CornerRadius(14),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect
                {
                    Color = accentColor,
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.6
                }
            };
            cellGrid.Children.Add(circleBorder);
        }

        // Day number TextBlock
        var textBlock = new TextBlock
        {
            Text = dayText,
            FontFamily = new FontFamily("pack://application:,,,/Fonts/#Outfit"),
            FontSize = 11.5,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Text color styling based on month/today status
        if (isToday)
        {
            textBlock.Foreground = Brushes.White;
        }
        else if (isCurrentMonth)
        {
            textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
        }
        else
        {
            // Dimmed color for trailing/leading days of neighboring months
            textBlock.Foreground = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF));
        }

        cellGrid.Children.Add(textBlock);
        DaysGrid.Children.Add(cellGrid);
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        UpdateCalendar();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        UpdateCalendar();
    }

    private bool _isClosing = false;

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        SafeClose();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SafeClose();
        }
    }

    private void SafeClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        bool isBottom = SettingsService.Instance.DockPosition == "Bottom";
        double finalTop;
        if (isBottom)
        {
            finalTop = DockWindow.Instance != null 
                ? DockWindow.Instance.Top - Height 
                : SystemParameters.PrimaryScreenHeight - Height - 56;
        }
        else
        {
            finalTop = DockWindow.Instance != null 
                ? DockWindow.Instance.Top + DockWindow.Instance.ActualHeight 
                : 56;
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

        // Force GC collection to immediately free resources of the calendar window
        GC.Collect();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
    }
}
