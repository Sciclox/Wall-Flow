using System;
using System.ComponentModel;
using System.Windows;

namespace WallFlow.Spotlight.Models;

public class AiChatMessage : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private bool _isUser;
    private string _content = string.Empty;
    private DateTime _timestamp = DateTime.Now;
    private bool _isLoading;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(nameof(Id)); }
    }

    public bool IsUser
    {
        get => _isUser;
        set
        {
            _isUser = value;
            OnPropertyChanged(nameof(IsUser));
            OnPropertyChanged(nameof(SenderName));
            OnPropertyChanged(nameof(UserMessageVisibility));
            OnPropertyChanged(nameof(AiMessageVisibility));
            OnPropertyChanged(nameof(ActionButtonsVisibility));
        }
    }

    public string SenderName => IsUser ? "Tú" : "Gemini AI";

    public Visibility UserMessageVisibility => IsUser ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AiMessageVisibility => !IsUser ? Visibility.Visible : Visibility.Collapsed;

    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(nameof(Content)); }
    }

    public DateTime Timestamp
    {
        get => _timestamp;
        set
        {
            _timestamp = value;
            OnPropertyChanged(nameof(Timestamp));
            OnPropertyChanged(nameof(FormattedTime));
        }
    }

    public string FormattedTime => Timestamp.ToString("hh:mm tt");

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(LoadingVisibility));
            OnPropertyChanged(nameof(ContentVisibility));
            OnPropertyChanged(nameof(ActionButtonsVisibility));
        }
    }

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentVisibility => IsLoading ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ActionButtonsVisibility => !IsUser && !IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
