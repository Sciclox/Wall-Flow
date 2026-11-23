using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using WallFlow.Services;

namespace WallFlow.Spotlight.Services;

public record ThemePreset(
    Color Background,
    Color Border,
    double BorderThickness,
    Color Text,
    Color Placeholder,
    Color Icon,
    Color Accent,
    double HoverBackgroundA,
    double HoverBorderA,
    double ItemBackgroundA,
    double ItemForegroundA,
    double SecondaryTextA,
    double SeparatorA,
    Color SearchFocus,
    bool IsLight = false
);

public class SpotlightThemeBrushes
{
    public Brush BgBrush { get; set; } = Brushes.Transparent;
    public Brush BorderBrush { get; set; } = Brushes.Transparent;
    public Thickness BorderThickness { get; set; } = new(0);
    public Brush TextBrush { get; set; } = Brushes.White;
    public Brush PlaceholderBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
    public Brush IconBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
    public Brush SelectedBorderBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));
    public Brush SelectedBackgroundBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
    public Brush HoverBackgroundBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
    public Brush HoverBorderBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
    public Brush ItemBackgroundBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
    public Brush ItemForegroundBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xA5, 0xFF, 0xFF, 0xFF));
    public Brush SecondaryTextBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
    public Brush SeparatorBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
    public Brush SearchFocusBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x22, 0x26));
    public double Width { get; set; } = 600.0;
    public CornerRadius CornerRadius { get; set; } = new(27.0);
    public Visibility FavoritesVisibility { get; set; } = Visibility.Visible;
}

public interface ISpotlightThemeService
{
    SpotlightThemeBrushes ComputeTheme(AppSettings settings, ResourceDictionary? appResources = null);
    bool TryParseColor(string hex, out Color color);
}

/// <summary>
/// Service managing visual styles, color presets, and theme brushes for the Spotlight UI.
/// </summary>
public class SpotlightThemeService : ISpotlightThemeService
{
    private static SpotlightThemeService? _instance;
    public static SpotlightThemeService Instance => _instance ??= new SpotlightThemeService();

    public static readonly Dictionary<string, ThemePreset> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Oscuro"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x0B, 0x0B, 0x0B),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x00, 0x7A, 0xFF),
            HoverBackgroundA: 0x15, HoverBorderA: 0x20,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x22, 0x22, 0x26)
        ),
        ["Claro"] = new ThemePreset(
            Background: Color.FromArgb(0xE6, 0xF0, 0xF0, 0xF0),
            Border: Color.FromArgb(0x40, 0x00, 0x00, 0x00),
            BorderThickness: 1,
            Text: Color.FromRgb(0x1A, 0x1A, 0x1A),
            Placeholder: Color.FromArgb(0x50, 0x00, 0x00, 0x00),
            Icon: Color.FromArgb(0x60, 0x00, 0x00, 0x00),
            Accent: Color.FromRgb(0x00, 0x7A, 0xFF),
            HoverBackgroundA: 0x15, HoverBorderA: 0x20,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0xE6, 0xE6, 0xE6),
            IsLight: true
        ),
        ["Midnight"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x0A, 0x0E, 0x1A),
            Border: Color.FromArgb(0x30, 0x4A, 0x6C, 0xF7),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x4A, 0x6C, 0xF7),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x0F, 0x13, 0x20)
        ),
        ["Ocean"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x06, 0x1A, 0x2E),
            Border: Color.FromArgb(0x30, 0x00, 0xB4, 0xD8),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x00, 0xB4, 0xD8),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x08, 0x1E, 0x32)
        ),
        ["Forest"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x06, 0x18, 0x0C),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x22, 0xC5, 0x5E),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x0A, 0x1E, 0x10)
        ),
        ["Sunset"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1A, 0x0E, 0x06),
            Border: Color.FromArgb(0x30, 0xF9, 0x73, 0x16),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0xF9, 0x73, 0x16),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x20, 0x12, 0x0A)
        ),
        ["Crimson"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1A, 0x06, 0x0A),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0xEF, 0x44, 0x44),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x20, 0x0A, 0x0E)
        ),
        ["Teal"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x06, 0x1A, 0x1A),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x14, 0xB8, 0xA6),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x0A, 0x20, 0x20)
        ),
        ["Warm"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1A, 0x14, 0x06),
            Border: Color.FromArgb(0x30, 0xF5, 0x9E, 0x0B),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0xF5, 0x9E, 0x0B),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x20, 0x1A, 0x0A)
        ),
        ["Rose"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1A, 0x06, 0x12),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0xEC, 0x48, 0x99),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x20, 0x0A, 0x18)
        ),
        ["Slate"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x1E, 0x29, 0x3B),
            Border: Color.FromArgb(0x30, 0x94, 0xA3, 0xB8),
            BorderThickness: 1,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x94, 0xA3, 0xB8),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x28, 0x35, 0x45)
        ),
        ["Violet"] = new ThemePreset(
            Background: Color.FromArgb(0xB4, 0x12, 0x06, 0x1E),
            Border: Colors.Transparent,
            BorderThickness: 0,
            Text: Colors.White,
            Placeholder: Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
            Icon: Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF),
            Accent: Color.FromRgb(0x8B, 0x5C, 0xF6),
            HoverBackgroundA: 0x18, HoverBorderA: 0x25,
            ItemBackgroundA: 0x12, ItemForegroundA: 0xA5,
            SecondaryTextA: 0xA0, SeparatorA: 0x15,
            SearchFocus: Color.FromArgb(0xE6, 0x18, 0x0A, 0x24)
        )
    };

    public SpotlightThemeBrushes ComputeTheme(AppSettings settings, ResourceDictionary? appResources = null)
    {
        var result = new SpotlightThemeBrushes
        {
            Width = settings.SpotlightWidth > 0 ? settings.SpotlightWidth : 600.0,
            CornerRadius = new CornerRadius(settings.SpotlightCornerRadius >= 0 ? settings.SpotlightCornerRadius : 27.0),
            FavoritesVisibility = settings.SpotlightShowFavorites ? Visibility.Visible : Visibility.Collapsed
        };

        if (settings.SpotlightThemeMode == "Dock")
        {
            result.BgBrush = appResources?["DockBackgroundBrush"] as Brush ?? new SolidColorBrush(Color.FromArgb(0xB4, 0x0B, 0x0B, 0x0B));
            result.BorderBrush = appResources?["DockBorderBrush"] as Brush ?? Brushes.Transparent;
            result.BorderThickness = new Thickness(settings.SpotlightBorderThickness >= 0 ? settings.SpotlightBorderThickness : 0.0);
            result.SelectedBorderBrush = appResources?["AccentBrush"] as Brush ?? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));
            result.SelectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            result.TextBrush = Brushes.White;
            result.PlaceholderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
            result.IconBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

            ApplySubBrushes(result, isLight: false);
            result.SearchFocusBrush = result.BgBrush;
        }
        else if (settings.SpotlightThemeMode == "Personalizado")
        {
            if (!string.IsNullOrEmpty(settings.SpotlightBackgroundColor) && TryParseColor(settings.SpotlightBackgroundColor, out var bgColor))
                result.BgBrush = new SolidColorBrush(bgColor);
            else
                result.BgBrush = appResources?["DockBackgroundBrush"] as Brush ?? new SolidColorBrush(Color.FromArgb(0xB4, 0x0B, 0x0B, 0x0B));

            if (!string.IsNullOrEmpty(settings.SpotlightBorderColor) && TryParseColor(settings.SpotlightBorderColor, out var borderColor))
                result.BorderBrush = new SolidColorBrush(borderColor);
            else
                result.BorderBrush = Brushes.Transparent;

            result.BorderThickness = new Thickness(settings.SpotlightBorderThickness >= 0 ? settings.SpotlightBorderThickness : 0.0);

            if (!string.IsNullOrEmpty(settings.SpotlightTextColor) && TryParseColor(settings.SpotlightTextColor, out var textColor))
                result.TextBrush = new SolidColorBrush(textColor);
            else
                result.TextBrush = Brushes.White;

            if (!string.IsNullOrEmpty(settings.SpotlightPlaceholderColor) && TryParseColor(settings.SpotlightPlaceholderColor, out var placeholderColor))
                result.PlaceholderBrush = new SolidColorBrush(placeholderColor);
            else
                result.PlaceholderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));

            if (!string.IsNullOrEmpty(settings.SpotlightIconColor) && TryParseColor(settings.SpotlightIconColor, out var iconColor))
                result.IconBrush = new SolidColorBrush(iconColor);
            else
                result.IconBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

            if (!string.IsNullOrEmpty(settings.SpotlightAccentColor) && TryParseColor(settings.SpotlightAccentColor, out var accentColor))
            {
                result.SelectedBorderBrush = new SolidColorBrush(accentColor);
                result.SelectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x25, accentColor.R, accentColor.G, accentColor.B));
            }
            else
            {
                result.SelectedBorderBrush = appResources?["AccentBrush"] as Brush ?? new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));
                result.SelectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
            }

            result.HoverBackgroundBrush = ResolveBrushOrDefault(settings.SpotlightHoverBackgroundColor, new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)));
            result.HoverBorderBrush = ResolveBrushOrDefault(settings.SpotlightHoverBorderColor, new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)));
            result.ItemBackgroundBrush = ResolveBrushOrDefault(settings.SpotlightItemBackgroundColor, new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)));
            result.ItemForegroundBrush = ResolveBrushOrDefault(settings.SpotlightItemForegroundColor, new SolidColorBrush(Color.FromArgb(0xA5, 0xFF, 0xFF, 0xFF)));
            result.SecondaryTextBrush = ResolveBrushOrDefault(settings.SpotlightSecondaryTextColor, new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)));
            result.SeparatorBrush = ResolveBrushOrDefault(settings.SpotlightSeparatorColor, new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)));
            result.SearchFocusBrush = ResolveBrushOrDefault(settings.SpotlightSearchFocusColor, new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x22, 0x26)));
        }
        else
        {
            ApplyThemePreset(result, settings.SpotlightThemeMode);
        }

        return result;
    }

    private Brush ResolveBrushOrDefault(string? hex, Brush defaultBrush)
    {
        if (!string.IsNullOrEmpty(hex) && TryParseColor(hex, out var color))
            return new SolidColorBrush(color);
        return defaultBrush;
    }

    private static void ApplyThemePreset(SpotlightThemeBrushes result, string mode)
    {
        if (!Presets.TryGetValue(mode, out var p)) return;

        result.BgBrush = new SolidColorBrush(p.Background);
        result.BorderBrush = new SolidColorBrush(p.Border);
        result.BorderThickness = new Thickness(p.BorderThickness);
        result.TextBrush = new SolidColorBrush(p.Text);
        result.PlaceholderBrush = new SolidColorBrush(p.Placeholder);
        result.IconBrush = new SolidColorBrush(p.Icon);
        result.SelectedBorderBrush = new SolidColorBrush(p.Accent);
        result.SelectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x25, p.Accent.R, p.Accent.G, p.Accent.B));
        result.HoverBackgroundBrush = new SolidColorBrush(Color.FromArgb((byte)p.HoverBackgroundA, p.Accent.R, p.Accent.G, p.Accent.B));
        result.HoverBorderBrush = new SolidColorBrush(Color.FromArgb((byte)p.HoverBorderA, p.Accent.R, p.Accent.G, p.Accent.B));

        byte baseC = p.IsLight ? (byte)0x00 : (byte)0xFF;
        result.ItemBackgroundBrush = new SolidColorBrush(Color.FromArgb((byte)p.ItemBackgroundA, baseC, baseC, baseC));
        result.ItemForegroundBrush = new SolidColorBrush(Color.FromArgb((byte)p.ItemForegroundA, baseC, baseC, baseC));
        result.SecondaryTextBrush = new SolidColorBrush(Color.FromArgb((byte)p.SecondaryTextA, baseC, baseC, baseC));
        result.SeparatorBrush = new SolidColorBrush(Color.FromArgb((byte)p.SeparatorA, baseC, baseC, baseC));
        result.SearchFocusBrush = new SolidColorBrush(p.Background);
    }

    private static void ApplySubBrushes(SpotlightThemeBrushes result, bool isLight)
    {
        if (isLight)
        {
            result.HoverBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x15, 0x00, 0x00, 0x00));
            result.HoverBorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0x00));
            result.ItemBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0x00, 0x00));
            result.ItemForegroundBrush = new SolidColorBrush(Color.FromArgb(0xA5, 0x00, 0x00, 0x00));
            result.SecondaryTextBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0x00, 0x00, 0x00));
            result.SeparatorBrush = new SolidColorBrush(Color.FromArgb(0x15, 0x00, 0x00, 0x00));
            result.SearchFocusBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xE6, 0xE6, 0xE6));
        }
        else
        {
            result.HoverBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            result.HoverBorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            result.ItemBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            result.ItemForegroundBrush = new SolidColorBrush(Color.FromArgb(0xA5, 0xFF, 0xFF, 0xFF));
            result.SecondaryTextBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));
            result.SeparatorBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            result.SearchFocusBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0x22, 0x22, 0x26));
        }
    }

    public bool TryParseColor(string hex, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted != null)
            {
                color = (Color)converted;
                return true;
            }
        }
        catch { /* ignored */ }
        return false;
    }
}
