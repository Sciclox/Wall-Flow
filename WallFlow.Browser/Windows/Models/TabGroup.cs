using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace WallFlow.Windows;

public class TabGroup : INotifyPropertyChanged
{
    private string _name = "Grupo 1";
    private bool _isActive = false;
    private bool _isEditing = false;

    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(nameof(IsActive)); }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); }
    }

    public ObservableCollection<BrowserTab> Tabs { get; set; } = new();
    public BrowserTab? ActiveTab { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
