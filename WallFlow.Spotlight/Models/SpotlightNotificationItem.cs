using System;
using System.ComponentModel;
using System.Windows.Media;

namespace WallFlow.Windows;

public class SpotlightNotificationItem : INotifyPropertyChanged
{
    public uint Id { get; set; }
    public string AppName { get; set; } = "Sistema";
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public ImageSource? AppIcon { get; set; }
    public string AppGlyph { get; set; } = "\uEA8F";
    public bool HasAppIcon => AppIcon != null;
    public bool HasNoAppIcon => AppIcon == null;
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
