using System;
using System.ComponentModel;
using System.Windows.Media;
using WallFlow.Services;

namespace WallFlow.Windows;

public class BrowserTab : INotifyPropertyChanged
{
    private string _title = "Nueva pestaña";
    private string _url = "about:blank";
    private bool _isEditing = false;
    private bool _hasCustomTitle = false;

    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(nameof(Title)); }
    }

    public string Url
    {
        get => _url;
        set { _url = value; OnPropertyChanged(nameof(Url)); }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); }
    }

    public bool HasCustomTitle
    {
        get => _hasCustomTitle;
        set { _hasCustomTitle = value; OnPropertyChanged(nameof(HasCustomTitle)); }
    }

    public CompositionWebViewTab? WebView { get; set; }
    public ImageSource? Favicon { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
