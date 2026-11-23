using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WallFlow.Search;

/// <summary>
/// Intelligent fuzzy matching engine for search queries.
/// Supports exact prefix, word boundary, acronym/initials, alias matching, and typo-tolerant Levenshtein distance.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Common command aliases and synonyms (query alias -> target keywords)
    /// </summary>
    private static readonly Dictionary<string, string[]> CommonAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calc"] = new[] { "calculadora", "calculator" },
        ["cmd"] = new[] { "simbolo del sistema", "command prompt", "terminal", "consola" },
        ["wt"] = new[] { "windows terminal", "terminal", "powershell" },
        ["pwsh"] = new[] { "powershell", "terminal" },
        ["ps"] = new[] { "powershell" },
        ["bash"] = new[] { "git bash", "wsl", "ubuntu", "terminal" },
        ["taskmgr"] = new[] { "administrador de tareas", "task manager" },
        ["control"] = new[] { "panel de control", "control panel" },
        ["paint"] = new[] { "paint", "mspaint" },
        ["mspaint"] = new[] { "paint" },
        ["notepad"] = new[] { "bloc de notas", "notepad" },
        ["regedit"] = new[] { "editor del registro", "registry editor" },
        ["explorer"] = new[] { "explorador de archivos", "file explorer" },
        ["word"] = new[] { "microsoft word", "word", "documento" },
        ["excel"] = new[] { "microsoft excel", "excel", "hoja de calculo" },
        ["ppt"] = new[] { "microsoft powerpoint", "powerpoint", "presentacion" },
        ["powerpoint"] = new[] { "microsoft powerpoint", "powerpoint" },
        ["chrome"] = new[] { "google chrome", "chrome" },
        ["edge"] = new[] { "microsoft edge", "edge" },
        ["brave"] = new[] { "brave browser", "brave" },
        ["firefox"] = new[] { "mozilla firefox", "firefox" },
        ["vsc"] = new[] { "visual studio code", "vscode" },
        ["vscode"] = new[] { "visual studio code" },
        ["vs"] = new[] { "visual studio" },
        ["config"] = new[] { "configuracion", "settings", "ajustes" },
        ["settings"] = new[] { "configuracion", "settings" },
        ["devmgmt"] = new[] { "administrador de dispositivos", "device manager" },
        ["diskmgmt"] = new[] { "administracion de discos", "disk management" },
        ["cleanmgr"] = new[] { "liberador de espacio", "disk cleanup" },
        ["snip"] = new[] { "recortes", "herramienta recortes", "snipping tool" },
        ["snipping"] = new[] { "recortes", "snipping tool" },
        ["whatsapp"] = new[] { "whatsapp", "whatsapp desktop" },
        ["whatsap"] = new[] { "whatsapp", "whatsapp desktop" },
        ["whatssap"] = new[] { "whatsapp", "whatsapp desktop" },
        ["wasap"] = new[] { "whatsapp", "whatsapp desktop" },
        ["wsp"] = new[] { "whatsapp", "whatsapp desktop" },
        ["wpp"] = new[] { "whatsapp", "whatsapp desktop" },
        ["whats"] = new[] { "whatsapp", "whatsapp desktop" },
        ["telegram"] = new[] { "telegram", "telegram desktop" },
        ["tg"] = new[] { "telegram", "telegram desktop" },
        ["discord"] = new[] { "discord" },
        ["dc"] = new[] { "discord" },
        ["spotify"] = new[] { "spotify" },
        ["spoti"] = new[] { "spotify" },
        ["spotifi"] = new[] { "spotify" },
        ["wifi"] = new[] { "red e internet", "configuracion de red", "conexiones de red", "network", "wi-fi" },
        ["red"] = new[] { "red e internet", "conexiones de red", "network", "ethernet", "wifi" },
        ["internet"] = new[] { "red e internet", "google chrome", "microsoft edge", "brave", "firefox" },
        ["ethernet"] = new[] { "red e internet", "conexiones de red" },
        ["bt"] = new[] { "bluetooth", "bluetooth y otros dispositivos" },
        ["bluetooth"] = new[] { "bluetooth y otros dispositivos", "bluetooth" },
        ["pantalla"] = new[] { "configuracion de pantalla", "pantalla", "display" },
        ["display"] = new[] { "configuracion de pantalla", "pantalla", "display" },
        ["resolucion"] = new[] { "configuracion de pantalla" },
        ["brillo"] = new[] { "configuracion de pantalla" },
        ["sonido"] = new[] { "configuracion de sonido", "dispositivos de sonido", "sonido", "sound" },
        ["sound"] = new[] { "configuracion de sonido", "dispositivos de sonido", "sonido", "sound" },
        ["volumen"] = new[] { "configuracion de sonido", "mezclador de volumen" },
        ["audio"] = new[] { "configuracion de sonido", "dispositivos de sonido" },
        ["microfono"] = new[] { "configuracion de sonido", "privacidad de microfono" },
        ["camara"] = new[] { "camara", "camera", "privacidad de camara" },
        ["camera"] = new[] { "camara", "camera" },
        ["actualizar"] = new[] { "windows update", "buscar actualizaciones" },
        ["actualizaciones"] = new[] { "windows update", "buscar actualizaciones" },
        ["update"] = new[] { "windows update", "buscar actualizaciones" },
        ["desinstalar"] = new[] { "programas y caracteristicas", "aplicaciones instaladas", "desinstalar un programa" },
        ["programas"] = new[] { "programas y caracteristicas", "aplicaciones instaladas" },
        ["bateria"] = new[] { "energia y bateria", "opciones de energia" },
        ["battery"] = new[] { "energia y bateria", "opciones de energia" },
        ["energia"] = new[] { "energia y bateria", "opciones de energia" },
        ["suspension"] = new[] { "energia y bateria", "suspender equipo" },
        ["almacenamiento"] = new[] { "almacenamiento", "liberador de espacio en disco" },
        ["disco"] = new[] { "administracion de discos", "almacenamiento", "liberador de espacio" },
        ["discos"] = new[] { "administracion de discos" },
        ["dispositivos"] = new[] { "administrador de dispositivos", "device manager" },
        ["drivers"] = new[] { "administrador de dispositivos" },
        ["servicios"] = new[] { "servicios", "services" },
        ["services"] = new[] { "servicios", "services" },
        ["eventos"] = new[] { "visor de eventos" },
        ["tareas"] = new[] { "administrador de tareas", "programador de tareas" },
        ["navegador"] = new[] { "google chrome", "microsoft edge", "brave", "firefox", "opera" },
        ["browser"] = new[] { "google chrome", "microsoft edge", "brave", "firefox", "opera" },
        ["musica"] = new[] { "spotify", "youtube music", "reproductor multimedia", "groove" },
        ["music"] = new[] { "spotify", "youtube music", "reproductor multimedia" },
        ["correo"] = new[] { "correo", "outlook", "mail" },
        ["mail"] = new[] { "correo", "outlook", "mail" },
        ["fotos"] = new[] { "fotos", "visualizador de fotos", "photos" },
        ["photos"] = new[] { "fotos", "photos" },
        ["archivos"] = new[] { "explorador de archivos", "file explorer" },
        ["carpeta"] = new[] { "explorador de archivos" },
        ["carpetas"] = new[] { "explorador de archivos" },
        ["mouse"] = new[] { "propiedades del mouse", "raton" },
        ["raton"] = new[] { "propiedades del mouse", "raton" },
        ["teclado"] = new[] { "teclado en pantalla", "idioma y region" },
        ["reloj"] = new[] { "fecha y hora", "reloj" },
        ["hora"] = new[] { "fecha y hora" },
        ["fecha"] = new[] { "fecha y hora" },
        ["fondo"] = new[] { "personalizacion y temas", "fondo de pantalla" },
        ["wallpaper"] = new[] { "personalizacion y temas", "wallflow" },
        ["tema"] = new[] { "personalizacion y temas", "colores y modo oscuro" },
        ["impresora"] = new[] { "impresoras y escaneres" },
        ["printer"] = new[] { "impresoras y escaneres" },
        ["firewall"] = new[] { "firewall de windows defender" },
        ["antivirus"] = new[] { "seguridad de windows", "windows security" },
        ["seguridad"] = new[] { "seguridad de windows", "privacidad y seguridad" }
    };

    /// <summary>
    /// Calculates a match score for a given target name against a user search query.
    /// Supports exact prefix, word boundary, acronym, alias, and keyword matching.
    /// Returns 0 if there is no acceptable match.
    /// </summary>
    public static int CalculateScore(string target, string query, string? path = null, IEnumerable<string>? keywords = null)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(query))
            return 0;

        string normTarget = RemoveDiacritics(target).Trim().ToLowerInvariant();
        string normQuery = RemoveDiacritics(query).Trim().ToLowerInvariant();

        if (normTarget.Length == 0 || normQuery.Length == 0)
            return 0;

        // 1. Exact match
        if (normTarget.Equals(normQuery, StringComparison.Ordinal))
            return 200;

        // 2. Starts with query
        if (normTarget.StartsWith(normQuery, StringComparison.Ordinal))
        {
            // Bonus if query is close in length to target
            int lenDiff = Math.Max(0, 30 - (normTarget.Length - normQuery.Length));
            return 120 + lenDiff;
        }

        // 3. Check alias dictionary
        int aliasScore = CheckAliasMatch(normTarget, normQuery);
        if (aliasScore > 0)
            return aliasScore;

        // 3.5. Check keywords/tags match
        if (keywords != null)
        {
            foreach (var kw in keywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                string normKw = RemoveDiacritics(kw).Trim().ToLowerInvariant();
                if (normKw.Equals(normQuery, StringComparison.Ordinal))
                    return 140;
                if (normKw.StartsWith(normQuery, StringComparison.Ordinal))
                    return 110;
                if (normKw.Contains(normQuery, StringComparison.Ordinal))
                    return 85;
            }
        }

        // 4. Word boundary match & Initials
        var words = normTarget.Split(new[] { ' ', '-', '_', '.', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);

        // Check initials / acronym (e.g. "vsc" for "visual studio code")
        if (words.Length >= 2 && normQuery.Length >= 2)
        {
            var sbInitials = new StringBuilder(words.Length);
            foreach (var w in words)
            {
                if (w.Length > 0) sbInitials.Append(w[0]);
            }
            string initials = sbInitials.ToString();

            if (initials.Equals(normQuery, StringComparison.Ordinal))
                return 110;

            if (initials.StartsWith(normQuery, StringComparison.Ordinal))
                return 95;

            if (initials.Contains(normQuery, StringComparison.Ordinal))
                return 80;
        }

        // Check if any word starts with query
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            if (w.StartsWith(normQuery, StringComparison.Ordinal))
            {
                // Words closer to the front rank higher
                int positionPenalty = Math.Min(i * 3, 20);
                return 95 - positionPenalty;
            }
        }

        // 5. Check executable filename or path match if available
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                string rawFileName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(rawFileName))
                {
                    string normFileName = RemoveDiacritics(rawFileName).ToLowerInvariant();
                    if (normFileName.Equals(normQuery, StringComparison.Ordinal))
                        return 100;
                    if (normFileName.StartsWith(normQuery, StringComparison.Ordinal))
                        return 85;
                    if (normFileName.Contains(normQuery, StringComparison.Ordinal))
                        return 60;
                }
            }
            catch { /* ignored */ }
        }

        // 6. Substring match
        int substrIdx = normTarget.IndexOf(normQuery, StringComparison.Ordinal);
        if (substrIdx >= 0)
        {
            // Bonus if it occurs early in the string
            int penalty = Math.Min(substrIdx, 25);
            return 65 - penalty;
        }

        // 7. Subsequence match (e.g. "chr" matches "[ch]rome", "pwsh" matches "[p]o[w]er[sh]ell")
        if (IsSubsequence(normQuery, normTarget, out int matchSpread))
        {
            int score = 55 - Math.Min(matchSpread, 25);
            if (score > 30)
                return score;
        }

        // 8. Typo-tolerant Levenshtein distance (for queries with at least 3 characters)
        if (normQuery.Length >= 3)
        {
            int maxDistance = normQuery.Length <= 4 ? 1 : 2;

            // Check whole target if short
            if (Math.Abs(normTarget.Length - normQuery.Length) <= maxDistance)
            {
                int dist = LevenshteinDistance(normQuery, normTarget, maxDistance);
                if (dist <= maxDistance)
                {
                    int bonus = (normTarget[0] == normQuery[0]) ? 10 : 0;
                    return 60 - (dist * 15) + bonus;
                }
            }

            // Check against each word in the target
            foreach (var w in words)
            {
                if (Math.Abs(w.Length - normQuery.Length) <= maxDistance)
                {
                    int dist = LevenshteinDistance(normQuery, w, maxDistance);
                    if (dist <= maxDistance)
                    {
                        int bonus = (w[0] == normQuery[0]) ? 10 : 0;
                        return 55 - (dist * 15) + bonus;
                    }
                }
            }
        }

        return 0;
    }

    private static int CheckAliasMatch(string normTarget, string normQuery)
    {
        if (CommonAliases.TryGetValue(normQuery, out var aliases))
        {
            foreach (var alias in aliases)
            {
                string normAlias = RemoveDiacritics(alias).ToLowerInvariant();
                if (normTarget.Equals(normAlias, StringComparison.Ordinal))
                    return 115;
                if (normTarget.StartsWith(normAlias, StringComparison.Ordinal))
                    return 110;
                if (normTarget.Contains(normAlias, StringComparison.Ordinal))
                    return 90;
            }
        }
        return 0;
    }

    private static bool IsSubsequence(string query, string target, out int spread)
    {
        spread = 0;
        int qIdx = 0;
        int firstMatch = -1;
        int lastMatch = -1;

        for (int tIdx = 0; tIdx < target.Length && qIdx < query.Length; tIdx++)
        {
            if (target[tIdx] == query[qIdx])
            {
                if (firstMatch == -1) firstMatch = tIdx;
                lastMatch = tIdx;
                qIdx++;
            }
        }

        if (qIdx == query.Length)
        {
            spread = lastMatch - firstMatch;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates Levenshtein distance with an early exit threshold.
    /// </summary>
    public static int LevenshteinDistance(string s, string t, int maxDistance = int.MaxValue)
    {
        int n = s.Length;
        int m = t.Length;

        if (n == 0) return m;
        if (m == 0) return n;
        if (Math.Abs(n - m) > maxDistance) return maxDistance + 1;

        int[] v0 = new int[m + 1];
        int[] v1 = new int[m + 1];

        for (int i = 0; i <= m; i++)
            v0[i] = i;

        for (int i = 0; i < n; i++)
        {
            v1[0] = i + 1;
            int minVal = v1[0];

            for (int j = 0; j < m; j++)
            {
                int cost = (s[i] == t[j]) ? 0 : 1;
                v1[j + 1] = Math.Min(
                    Math.Min(v1[j] + 1, v0[j + 1] + 1),
                    v0[j] + cost
                );
                minVal = Math.Min(minVal, v1[j + 1]);
            }

            if (minVal > maxDistance)
                return maxDistance + 1;

            var temp = v0;
            v0 = v1;
            v1 = temp;
        }

        return v0[m];
    }

    /// <summary>
    /// Strips accents/diacritics from text so 'á' matches 'a', 'é' matches 'e', etc.
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }
}
