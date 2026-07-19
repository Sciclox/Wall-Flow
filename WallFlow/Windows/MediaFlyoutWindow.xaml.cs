using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallFlow.Services;

namespace WallFlow.Windows;

public partial class MediaFlyoutWindow : Window
{
    private readonly global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? _session;
    private System.Threading.CancellationTokenSource? _updateCts;
    private bool _isClosing = false;

    private readonly double _targetLeft;

    public MediaFlyoutWindow(global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession? session, double left)
    {
        InitializeComponent();
        _session = session;
        _targetLeft = left;

        if (_session != null)
        {
            _session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
            _session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply acrylic blur
        BackdropHelper.ApplyAcrylic(this);
        BackdropHelper.UpdateWindowRegion(this, 0);

        // Position window relative to Media player section of the dock
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

        UpdateDetails();
    }

    private void Session_MediaPropertiesChanged(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession sender,
        global::Windows.Media.Control.MediaPropertiesChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(UpdateDetails);
    }

    private void Session_PlaybackInfoChanged(
        global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession sender,
        global::Windows.Media.Control.PlaybackInfoChangedEventArgs args)
    {
        Dispatcher.BeginInvoke(UpdateDetails);
    }

    private async void UpdateDetails()
    {
        _updateCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _updateCts = cts;
        var token = cts.Token;

        if (_session == null)
        {
            SafeClose();
            return;
        }

        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            if (token.IsCancellationRequested) return;

            var playback = _session.GetPlaybackInfo();

            if (props == null || string.IsNullOrEmpty(props.Title))
            {
                SafeClose();
                return;
            }

            TitleText.Text = props.Title;
            ArtistText.Text = props.Artist;

            // Set play/pause button state (Play: \uE768, Pause: \uE769)
            bool isPlaying = playback != null && playback.PlaybackStatus == global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            PlayPauseButton.Content = isPlaying ? "\uE769" : "\uE768";

            // Load album art thumbnail as background overlay
            if (props.Thumbnail != null)
            {
                try
                {
                    using (var stream = await props.Thumbnail.OpenReadAsync())
                    {
                        if (token.IsCancellationRequested) return;

                        using (var memStream = new MemoryStream())
                        {
                            await stream.AsStreamForRead().CopyToAsync(memStream);
                            if (token.IsCancellationRequested) return;
                            memStream.Position = 0;

                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = memStream;
                            bmp.EndInit();
                            bmp.Freeze();

                            BgImageBrush.ImageSource = bmp;
                            BgImageBorder.Visibility = Visibility.Visible;
                        }
                    }
                }
                catch
                {
                    BgImageBorder.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                BgImageBorder.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            SafeClose();
        }
    }

    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_session != null)
        {
            try { await _session.TrySkipPreviousAsync(); } catch { }
        }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_session != null)
        {
            try { await _session.TryTogglePlayPauseAsync(); } catch { }
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_session != null)
        {
            try { await _session.TrySkipNextAsync(); } catch { }
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        SafeClose();
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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unregister events to avoid leaks
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
            _session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
        }
        base.OnClosed(e);

        // Force GC collection to immediately free the media album art BitmapImages
        GC.Collect();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
    }
}
