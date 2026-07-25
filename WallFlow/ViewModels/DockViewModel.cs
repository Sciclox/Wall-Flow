using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace WallFlow.ViewModels;

public partial class DockViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isLocalGalleryOpen;

    [ObservableProperty]
    private bool _isOnlineGalleryOpen;

    [ObservableProperty]
    private bool _isYouTubeMusicOpen;

    [ObservableProperty]
    private bool _isChatGptOpen;

    [ObservableProperty]
    private bool _isGeminiOpen;

    [ObservableProperty]
    private bool _isAiAssistantOpen;

    [ObservableProperty]
    private bool _isPowerWindowOpen;

    [ObservableProperty]
    private bool _isMutedActive;

    [ObservableProperty]
    private bool _isPinnedFlyoutOpen;

    [ObservableProperty]
    private bool _isSettingsOpen;

    partial void OnIsLocalGalleryOpenChanged(bool value) => CheckAutoHide();
    partial void OnIsOnlineGalleryOpenChanged(bool value) => CheckAutoHide();
    partial void OnIsYouTubeMusicOpenChanged(bool value) => CheckAutoHide();
    partial void OnIsChatGptOpenChanged(bool value) => CheckAutoHide();
    partial void OnIsGeminiOpenChanged(bool value) => CheckAutoHide();
    partial void OnIsAiAssistantOpenChanged(bool value) => CheckAutoHide();
    partial void OnIsPowerWindowOpenChanged(bool value) => CheckAutoHide();
    partial void OnIsPinnedFlyoutOpenChanged(bool value) => CheckAutoHide();
    partial void OnIsSettingsOpenChanged(bool value) => CheckAutoHide();

    private void CheckAutoHide()
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
            DockWindow.Instance?.CheckAutoHide();
        }));
    }
}
