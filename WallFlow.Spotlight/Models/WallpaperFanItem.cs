using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace WallFlow.Windows;

public class WallpaperFanItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private string _displayTitle = string.Empty;
    private string _resolutionText = string.Empty;
    private string _fileSizeText = string.Empty;
    private string _badgeText = "4K UHD";
    private ImageSource? _thumbnail;
    private double _translateX;
    private double _translateY;
    private double _rotationAngle;
    private double _scale = 1.0;
    private double _opacity = 1.0;
    private int _zIndex = 10;
    private bool _isHero;
    private Visibility _visibility = Visibility.Visible;

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value) return;
            _filePath = value;
            OnPropertyChanged(nameof(FilePath));
        }
    }

    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName == value) return;
            _fileName = value;
            OnPropertyChanged(nameof(FileName));
        }
    }

    public string DisplayTitle
    {
        get => _displayTitle;
        set
        {
            if (_displayTitle == value) return;
            _displayTitle = value;
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public string ResolutionText
    {
        get => _resolutionText;
        set
        {
            if (_resolutionText == value) return;
            _resolutionText = value;
            OnPropertyChanged(nameof(ResolutionText));
        }
    }

    public string FileSizeText
    {
        get => _fileSizeText;
        set
        {
            if (_fileSizeText == value) return;
            _fileSizeText = value;
            OnPropertyChanged(nameof(FileSizeText));
        }
    }

    public string BadgeText
    {
        get => _badgeText;
        set
        {
            if (_badgeText == value) return;
            _badgeText = value;
            OnPropertyChanged(nameof(BadgeText));
        }
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            OnPropertyChanged(nameof(Thumbnail));
        }
    }

    public double TranslateX
    {
        get => _translateX;
        set
        {
            if (Math.Abs(_translateX - value) < 0.01) return;
            _translateX = value;
            OnPropertyChanged(nameof(TranslateX));
        }
    }

    public double TranslateY
    {
        get => _translateY;
        set
        {
            if (Math.Abs(_translateY - value) < 0.01) return;
            _translateY = value;
            OnPropertyChanged(nameof(TranslateY));
        }
    }

    public double RotationAngle
    {
        get => _rotationAngle;
        set
        {
            if (Math.Abs(_rotationAngle - value) < 0.01) return;
            _rotationAngle = value;
            OnPropertyChanged(nameof(RotationAngle));
        }
    }

    public double Scale
    {
        get => _scale;
        set
        {
            if (Math.Abs(_scale - value) < 0.001) return;
            _scale = value;
            OnPropertyChanged(nameof(Scale));
        }
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            if (Math.Abs(_opacity - value) < 0.01) return;
            _opacity = value;
            OnPropertyChanged(nameof(Opacity));
        }
    }

    public int ZIndex
    {
        get => _zIndex;
        set
        {
            if (_zIndex == value) return;
            _zIndex = value;
            OnPropertyChanged(nameof(ZIndex));
        }
    }

    public bool IsHero
    {
        get => _isHero;
        set
        {
            if (_isHero == value) return;
            _isHero = value;
            OnPropertyChanged(nameof(IsHero));
        }
    }

    public Visibility Visibility
    {
        get => _visibility;
        set
        {
            if (_visibility == value) return;
            _visibility = value;
            OnPropertyChanged(nameof(Visibility));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
