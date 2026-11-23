using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace WallFlow;

public partial class WallpaperEntry : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _isFavorite;
}
