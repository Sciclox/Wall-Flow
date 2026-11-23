using System;
using System.Threading.Tasks;
using System.Windows.Media;

namespace WallFlow.Services
{
    public interface IApplicationHost
    {
        double Left { get; }
        double Top { get; }
        double ActualWidth { get; }
        double ActualHeight { get; }
        bool IsPinnedFlyoutOpen { get; set; }
        void RecordWidgetClose(string widgetType);
        void ToggleWidget(string widgetType);
        void SearchMusic(string query);
        void PlayMusic(string trackIdOrQuery, bool showWindow = false);

        // Direct music playback control & state
        bool IsInternalMusicActive { get; }
        bool IsInternalMusicPlaying { get; }
        string? InternalMusicTitle { get; }
        string? InternalMusicArtist { get; }
        string? InternalMusicTime { get; }
        double InternalMusicProgress { get; }
        ImageSource? InternalMusicArtwork { get; }
        Task<bool> TogglePlayPauseMusicAsync();
        Task NextMusicTrackAsync();
        Task PreviousMusicTrackAsync();
        Task<bool> ToggleRepeatMusicAsync(bool? forceState = null);
        bool IsInternalMusicRepeat { get; }
        Task<string?> GetInternalMusicQueueJsonAsync();
        Task<bool> PlayInternalMusicQueueIndexAsync(int index, string? videoId = null);
        event EventHandler? InternalMusicStateChanged;
    }
}
