using System;
using System.Collections.Generic;

namespace WallFlow.Services;

public record DockThemeColors(
    string Accent,
    string Border1,
    string Border2,
    string Border3,
    string DockBg
);

public static class ThemePresets
{
    public static readonly IReadOnlyDictionary<string, DockThemeColors> DockThemes =
        new Dictionary<string, DockThemeColors>(StringComparer.OrdinalIgnoreCase)
        {
            ["cyberpunk"] = new("#FF0055", "#FF0055", "#00F0FF", "#FF0055", "#E60B0B0F"),
            ["dracula"] = new("#BD93F9", "#BD93F9", "#FF79C6", "#8BE9FD", "#F0282A36"),
            ["tokyo_night"] = new("#7AA2F7", "#BB9AF7", "#7AA2F7", "#2AC3DE", "#F01A1B26"),
            ["nord_frost"] = new("#88C0D0", "#81A1C1", "#88C0D0", "#8FBCBB", "#F02E3440"),
            ["rose_pine"] = new("#EBBCBA", "#9CCFD8", "#EBBCBA", "#F6C177", "#F5191724"),
            ["vaporwave"] = new("#FF007F", "#FF007F", "#7B00FF", "#00F5FF", "#E812001A"),
            ["glass_light"] = new("#007AFF", "#80FFFFFF", "#40FFFFFF", "#80FFFFFF", "#20FFFFFF"),
            ["emerald"] = new("#10B981", "#059669", "#34D399", "#10B981", "#E0061E14"),
            ["sunset"] = new("#FF5E3A", "#FF5E3A", "#FF2A68", "#FF5E3A", "#D90F0B1E"),
            ["solarized"] = new("#268BD2", "#859900", "#268BD2", "#2AA198", "#F0002B36"),
        };

    public static readonly IReadOnlyDictionary<string, string[]> GradientPresets =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["neon"] = new[] { "#00F5FF", "#BF00FF", "#FF006E" },
            ["sunset"] = new[] { "#FF6B35", "#FF2D87", "#D946EF" },
            ["ocean"] = new[] { "#007AFF", "#5856D6", "#AF52DE" },
            ["forest"] = new[] { "#059669", "#10B981", "#34D399" },
            ["fire"] = new[] { "#FF4500", "#FF0080", "#7B00FF" },
            ["solarized"] = new[] { "#859900", "#268BD2", "#2AA198" },
            ["aurora"] = new[] { "#06D6A0", "#118AB2", "#7B2FF7" },
            ["lava"] = new[] { "#FF4500", "#FF0080", "#7B00FF" },
            ["pastel"] = new[] { "#A78BFA", "#F472B6", "#FB923C" }
        };

    public static bool TryGetDockTheme(string name, out DockThemeColors? colors)
    {
        return DockThemes.TryGetValue(name, out colors);
    }

    public static bool TryGetGradientPreset(string name, out string[]? colors)
    {
        return GradientPresets.TryGetValue(name, out colors);
    }
}
