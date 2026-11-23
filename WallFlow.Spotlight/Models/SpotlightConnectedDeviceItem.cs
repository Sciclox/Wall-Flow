using System;
using System.ComponentModel;

namespace WallFlow.Windows;

public class SpotlightConnectedDeviceItem : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _category = string.Empty;
    private string _glyph = "\uE702";
    private string _statusText = "Conectado";
    private string _detailsText = string.Empty;
    private bool _isConnected = true;
    private bool _isDefault = false;
    private string? _batteryText;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(nameof(Id)); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    public string Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(nameof(Category)); }
    }

    public string Glyph
    {
        get => _glyph;
        set { _glyph = value; OnPropertyChanged(nameof(Glyph)); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
    }

    public string DetailsText
    {
        get => _detailsText;
        set { _detailsText = value; OnPropertyChanged(nameof(DetailsText)); OnPropertyChanged(nameof(HasDetails)); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(nameof(IsConnected)); }
    }

    public bool IsDefault
    {
        get => _isDefault;
        set { _isDefault = value; OnPropertyChanged(nameof(IsDefault)); }
    }

    public string? BatteryText
    {
        get => _batteryText;
        set { _batteryText = value; OnPropertyChanged(nameof(BatteryText)); OnPropertyChanged(nameof(HasBattery)); }
    }

    public bool HasBattery => !string.IsNullOrWhiteSpace(BatteryText);
    public bool HasDetails => !string.IsNullOrWhiteSpace(DetailsText);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
