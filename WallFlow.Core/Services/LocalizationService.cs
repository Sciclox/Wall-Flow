using System;
using System.Collections.Generic;
using System.Globalization;

namespace WallFlow.Services;

public class LocalizationService
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private readonly string _currentLang;
    private readonly Dictionary<string, Dictionary<string, string>> _translations = new();

    public LocalizationService()
    {
        // Detect system language (default to Spanish "es" if starts with "es", otherwise "en" fallback)
        string culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
        _currentLang = (culture == "es") ? "es" : "en";

        InitializeTranslations();
    }

    private void InitializeTranslations()
    {
        // Glossary of key labels
        var es = new Dictionary<string, string>
        {
            { "Settings_Title", "Ajustes de WallFlow" },
            { "Settings_Save", "Guardar" },
            { "Settings_General", "General" },
            { "Settings_Widgets", "Widgets" },
            { "Settings_Sound", "Sonido" },
            { "Settings_Hotkeys", "Atajos" },
            { "Settings_AutoHide", "Ocultar automáticamente el dock" },
            { "Settings_HideTaskbar", "Ocultar la barra de tareas de Windows" },
            { "Settings_DockPosition", "Posición del Dock" },
            { "Settings_BorderThickness", "Grosor del borde" },
            { "Settings_DockHeight", "Altura del Dock" },
            { "Settings_Monitor", "Monitor del Dock" },
            { "Settings_Font", "Tipografía de la aplicación" },
            { "Settings_SoundEnabled", "Efectos de sonido habilitados" },
            { "Settings_SoundVolume", "Volumen del sonido" },
            { "Settings_SoundProfile", "Perfil de sonido" },
            { "Dock_Tooltip_Settings", "Ajustes" },
            { "Dock_Tooltip_Gallery", "Galería Local" },
            { "Dock_Tooltip_Online", "Galería Online" },
            { "Dock_Tooltip_YouTube", "YouTube Music" },
            { "Dock_Tooltip_Power", "Encendido / Apagado" },
            { "Dock_Tooltip_RecycleBin", "Papelera de Reciclaje" },
            { "Dock_Tooltip_Volume", "Volumen" },
            { "Dock_Tooltip_ShowDesktop", "Mostrar Escritorio" },
            { "Msg_InvalidColor", "Por favor, corrige los colores inválidos antes de guardar." },
            { "Msg_ColorTitle", "Color Inválido" }
        };

        var en = new Dictionary<string, string>
        {
            { "Settings_Title", "WallFlow Settings" },
            { "Settings_Save", "Save" },
            { "Settings_General", "General" },
            { "Settings_Widgets", "Widgets" },
            { "Settings_Sound", "Sound" },
            { "Settings_Hotkeys", "Hotkeys" },
            { "Settings_AutoHide", "Auto-hide the dock" },
            { "Settings_HideTaskbar", "Hide the Windows taskbar" },
            { "Settings_DockPosition", "Dock Position" },
            { "Settings_BorderThickness", "Border thickness" },
            { "Settings_DockHeight", "Dock height" },
            { "Settings_Monitor", "Dock Monitor" },
            { "Settings_Font", "App Typography" },
            { "Settings_SoundEnabled", "Enable sound effects" },
            { "Settings_SoundVolume", "Sound Volume" },
            { "Settings_SoundProfile", "Sound Profile" },
            { "Dock_Tooltip_Settings", "Settings" },
            { "Dock_Tooltip_Gallery", "Local Gallery" },
            { "Dock_Tooltip_Online", "Online Gallery" },
            { "Dock_Tooltip_YouTube", "YouTube Music" },
            { "Dock_Tooltip_Power", "Power Options" },
            { "Dock_Tooltip_RecycleBin", "Recycle Bin" },
            { "Dock_Tooltip_Volume", "Volume" },
            { "Dock_Tooltip_ShowDesktop", "Show Desktop" },
            { "Msg_InvalidColor", "Please correct invalid colors before saving." },
            { "Msg_ColorTitle", "Invalid Color" }
        };

        _translations["es"] = es;
        _translations["en"] = en;
    }

    public string Get(string key)
    {
        if (_translations.TryGetValue(_currentLang, out var langDict) && langDict.TryGetValue(key, out var val))
        {
            return val;
        }
        
        // Fallback to English, then Spanish, then key name
        if (_translations.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
        {
            return enVal;
        }
        if (_translations.TryGetValue("es", out var esDict) && esDict.TryGetValue(key, out var esVal))
        {
            return esVal;
        }

        return key;
    }
}
