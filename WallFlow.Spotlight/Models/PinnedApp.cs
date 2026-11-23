using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace WallFlow.Windows;

/// <summary>
/// Data model representing an application, file, clipboard item, or AI response in Spotlight.
/// </summary>
public class PinnedApp : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private bool _isSelected;
    private bool _isPinned;
    private bool _isDragging;
    private bool _isRunning;

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            _isDragging = value;
            OnPropertyChanged(nameof(IsDragging));
            OnPropertyChanged(nameof(Opacity));
        }
    }

    public double Opacity => _isDragging ? 0.4 : 1.0;

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _initials = ComputeInitials(value);
            OnPropertyChanged(nameof(Name));
        }
    }

    private string _initials = string.Empty;
    public string Initials => _initials;

    private static string ComputeInitials(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return string.Empty;
        var sb = new System.Text.StringBuilder(words.Length);
        foreach (var w in words)
            if (w.Length > 0) sb.Append(char.ToLowerInvariant(w[0]));
        return sb.ToString();
    }

    private string _path = string.Empty;
    public string Path
    {
        get => _path;
        set
        {
            _path = value;
            OnPropertyChanged(nameof(Path));
            OnPropertyChanged(nameof(FriendlyPath));
            OnPropertyChanged(nameof(IsActionVisible));
            OnPropertyChanged(nameof(IsPinnable));
        }
    }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(IsIconVisible));
            OnPropertyChanged(nameof(IsGlyphVisible));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            _isPinned = value;
            OnPropertyChanged(nameof(IsPinned));
        }
    }

    public string FriendlyPath
    {
        get
        {
            if (Path.StartsWith("ytmusic:", StringComparison.OrdinalIgnoreCase))
            {
                return "YouTube Music";
            }
            if (Path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            {
                return "Configuración de Windows";
            }
            if (Path.EndsWith(".msc", StringComparison.OrdinalIgnoreCase))
            {
                return "Herramienta de Administración";
            }
            if (Path.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase))
            {
                return "Panel de Control";
            }
            if (Path.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase))
            {
                return "Aplicación de Windows Store";
            }
            if (Path == "ADD_APP")
            {
                return "Anclar una aplicación personalizada al menú";
            }
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (string.IsNullOrEmpty(dir)) return Path;
                return dir;
            }
            catch { return Path; }
        }
    }

    private bool _runAsAdmin;
    public bool RunAsAdmin
    {
        get => _runAsAdmin;
        set
        {
            _runAsAdmin = value;
            OnPropertyChanged(nameof(RunAsAdmin));
            OnPropertyChanged(nameof(ActionText));
        }
    }

    private string? _actionText;
    public string ActionText
    {
        get
        {
            if (!string.IsNullOrEmpty(_actionText)) return _actionText;
            return IsClipboardItem ? "Copiar" : (RunAsAdmin ? "Admin" : "Ejecutar");
        }
        set
        {
            _actionText = value;
            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(IsActionBadgeVisible));
        }
    }

    public string IconGlyph { get; set; } = string.Empty;
    public bool IsGlyphVisible => !string.IsNullOrEmpty(IconGlyph) && Icon == null;
    public bool IsIconVisible => Icon != null;
    public bool IsActionVisible => Path != "ADD_APP" && !Path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase);

    private string? _subtitle;
    public string? Subtitle
    {
        get => _subtitle;
        set
        {
            _subtitle = value;
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(IsSubtitleVisible));
        }
    }

    public bool IsSubtitleVisible => !string.IsNullOrEmpty(Subtitle);
    public string? ToolTip { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? VideoId { get; set; }
    public string? Duration { get; set; }
    public string? Channel { get; set; }
    public DateTime? Timestamp { get; set; }
    public List<string> Keywords { get; set; } = new();
    public string Category { get; set; } = "App";

    public Guid ClipboardId { get; set; } = Guid.Empty;
    public string ClipboardType { get; set; } = string.Empty;
    public bool IsClipboardItem => Path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase);
    public bool IsClipboardLink => IsClipboardItem && ClipboardType == "Link";
    public bool IsClipboardFile => IsClipboardItem && ClipboardType == "File";
    public bool IsClipboardText => IsClipboardItem && ClipboardType != "Link" && ClipboardType != "File" && ClipboardType != "Image";
    public bool IsClipboardImage => IsClipboardItem && ClipboardType == "Image";

    public bool IsAiItem => (ClipboardType != null && ClipboardType == "IA") ||
                           (Path != null && (Path == "AI_LOADING" || Path == "AI_ERROR" || Path == "AI_EMPTY")) ||
                           (Subtitle != null && Subtitle.StartsWith("Respuesta IA"));

    public bool IsActionBadgeVisible => !IsAiItem && !IsClipboardItem && !string.IsNullOrEmpty(ActionText);

    public bool IsPinnable => !IsAiItem && !string.IsNullOrEmpty(Path) && Path != "ADD_APP" && !Path.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
