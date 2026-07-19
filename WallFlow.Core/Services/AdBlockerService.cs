using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WallFlow.Services;

[Flags]
public enum AdBlockResourceType
{
    None = 0,
    Document = 1 << 0,
    Stylesheet = 1 << 1,
    Image = 1 << 2,
    Media = 1 << 3,
    Font = 1 << 4,
    Script = 1 << 5,
    XmlHttpRequest = 1 << 6,
    Subdocument = 1 << 7,
    Ping = 1 << 8,
    Other = 1 << 9,
    All = 0xFFFF
}

public class AdblockRule
{
    public string RawRule { get; set; } = string.Empty;
    public bool IsException { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public bool IsDomainAnchor { get; set; }
    public bool IsStartAnchor { get; set; }
    public bool IsEndAnchor { get; set; }
    
    // Options
    public bool ThirdPartyOnly { get; set; }
    public bool FirstPartyOnly { get; set; }
    public HashSet<string>? AllowedDomains { get; set; }
    public HashSet<string>? BlockedDomains { get; set; }
    public int ResourceTypeMask { get; set; }
    public string? FastToken { get; set; }

    private Regex? _patternRegex;
    private bool _regexInitialized = false;
    private readonly object _regexLock = new();

    public Regex? GetRegex()
    {
        if (_regexInitialized) return _patternRegex;
        lock (_regexLock)
        {
            if (!_regexInitialized)
            {
                _patternRegex = CompilePatternToRegex(Pattern, IsDomainAnchor, IsStartAnchor, IsEndAnchor);
                _regexInitialized = true;
            }
        }
        return _patternRegex;
    }

    private static Regex? CompilePatternToRegex(string pattern, bool isDomainAnchor, bool isStartAnchor, bool isEndAnchor)
    {
        if (!isDomainAnchor && !isStartAnchor && !isEndAnchor && !pattern.Contains('*') && !pattern.Contains('^'))
        {
            return null;
        }

        string escaped = Regex.Escape(pattern);
        escaped = escaped.Replace("\\*", ".*");
        escaped = escaped.Replace("\\^", "([^a-zA-Z0-9_\\-\\.\\%\\+]|$)");

        string regexStr = escaped;
        if (isDomainAnchor)
        {
            regexStr = "^(https?://)?([^/]+\\.)?" + escaped;
        }
        else if (isStartAnchor)
        {
            regexStr = "^" + escaped;
        }

        if (isEndAnchor)
        {
            regexStr = regexStr + "$";
        }

        try
        {
            // Do NOT use RegexOptions.Compiled to save memory
            return new Regex(regexStr, RegexOptions.IgnoreCase);
        }
        catch
        {
            return null;
        }
    }

    public bool Matches(string url, string requestHost, AdBlockResourceType resourceType, string documentHost, bool isThirdParty)
    {
        // 1. Check resource type flag
        if ((ResourceTypeMask & (int)resourceType) == 0)
            return false;

        // 2. Check third-party / first-party
        if (ThirdPartyOnly && !isThirdParty)
            return false;
        if (FirstPartyOnly && isThirdParty)
            return false;

        // 3. Check document domain constraints
        if (AllowedDomains != null && AllowedDomains.Count > 0)
        {
            bool allowed = false;
            foreach (var dom in AllowedDomains)
            {
                if (documentHost.Equals(dom, StringComparison.OrdinalIgnoreCase) || 
                    documentHost.EndsWith("." + dom, StringComparison.OrdinalIgnoreCase))
                {
                    allowed = true;
                    break;
                }
            }
            if (!allowed) return false;
        }
        if (BlockedDomains != null && BlockedDomains.Count > 0)
        {
            foreach (var dom in BlockedDomains)
            {
                if (documentHost.Equals(dom, StringComparison.OrdinalIgnoreCase) || 
                    documentHost.EndsWith("." + dom, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // 4. Check URL pattern matching
        var regex = GetRegex();
        if (IsDomainAnchor)
        {
            if (regex != null)
            {
                return regex.IsMatch(url);
            }
            else
            {
                bool hostMatches = requestHost.Equals(Pattern, StringComparison.OrdinalIgnoreCase) || 
                                   requestHost.EndsWith("." + Pattern, StringComparison.OrdinalIgnoreCase);
                return hostMatches;
            }
        }

        if (regex != null)
        {
            return regex.IsMatch(url);
        }
        else
        {
            return url.Contains(Pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public static class AdBlockerService
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallFlow");
    
    private static readonly string EasyListPath = Path.Combine(FolderPath, "easylist.txt");
    private static readonly string EasyPrivacyPath = Path.Combine(FolderPath, "easyprivacy.txt");
    private static readonly string CustomBlocklistPath = Path.Combine(FolderPath, "custom_blocklist.json");
    private static readonly string WhitelistPath = Path.Combine(FolderPath, "whitelist.json");
    
    private const string EasyListUrl = "https://easylist.to/easylist/easylist.txt";
    private const string EasyPrivacyUrl = "https://easylist.to/easylist/easyprivacy.txt";
    
    private static readonly HttpClient _httpClient = new();
    private static readonly object _lock = new();
    private static bool _isInitialized = false;

    private class AdBlockSnapshot
    {
        public List<AdblockRule> UniversalBlockingRules { get; } = new();
        public List<AdblockRule> UniversalExceptionRules { get; } = new();
        public Dictionary<string, List<AdblockRule>> BlockingRulesByToken { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<AdblockRule>> ExceptionRulesByToken { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Cosmetic rules
        public List<string> GlobalCosmeticRules { get; } = new();
        public Dictionary<string, List<string>> DomainCosmeticRules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> DomainCosmeticExceptions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static AdBlockSnapshot _activeSnapshot = new();

    // User whitelist
    private static HashSet<string> _whitelistedDomains = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> BadTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "net", "org", "http", "https", "www", "html", "js", "css", "gif", "png", "jpg", "jpeg", "xml"
    };

    // Hardcoded fallback offline rules (basic protection before download finishes)
    private static readonly string[] FallbackOfflineDomains = new[]
    {
        "doubleclick.net", "google-analytics.com", "googlesyndication.com",
        "googleadservices.com", "googletagservices.com", "googletagmanager.com",
        "adnxs.com", "amazon-adsystem.com", "scorecardresearch.com",
        "taboola.com", "outbrain.com", "criteo.com", "sentry.io"
    };

    static AdBlockerService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;
            _isInitialized = true;
        }

        // 1. Load user settings
        LoadWhitelist();

        // 2. Load basic fallback rules synchronously so we have basic protection immediately
        LoadFallbackRulesSync();

        // 3. Start background task to parse EasyList and Custom Blocklist asynchronously
        Task.Run(() =>
        {
            LoadFullRulesBackground();
        });
    }

    private static void LoadFallbackRulesSync()
    {
        try
        {
            var snapshot = new AdBlockSnapshot();
            LoadFallbackRules(snapshot);
            lock (_lock)
            {
                _activeSnapshot = snapshot;
            }
            System.Diagnostics.Debug.WriteLine("[AdBlocker] Loaded fallback rules synchronously.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error loading fallback rules synchronously: {ex.Message}");
        }
    }

    private static void LoadFallbackRules(AdBlockSnapshot snapshot)
    {
        foreach (var domain in FallbackOfflineDomains)
        {
            ParseNetworkRule("||" + domain, false, snapshot);
        }
        
        // Add basic cosmetic rules
        snapshot.GlobalCosmeticRules.Add(".adsbygoogle");
        snapshot.GlobalCosmeticRules.Add(".ad-container");
        snapshot.GlobalCosmeticRules.Add(".ad-image");
        snapshot.GlobalCosmeticRules.Add(".banner-ad");
        snapshot.GlobalCosmeticRules.Add(".sponsored-post");
    }

    private static void LoadFullRulesBackground()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[AdBlocker] Starting background rule parsing...");
            var snapshot = new AdBlockSnapshot();
            
            // 1. Add fallback rules to this snapshot
            LoadFallbackRules(snapshot);

            // 2. Parse EasyList and EasyPrivacy files
            bool hasList = File.Exists(EasyListPath);
            bool hasPrivacy = File.Exists(EasyPrivacyPath);
            
            if (hasList) ParseRulesFile(EasyListPath, snapshot);
            if (hasPrivacy) ParseRulesFile(EasyPrivacyPath, snapshot);

            // 3. Load custom user blocklist
            LoadCustomBlocklist(snapshot);

            // Atomic swap
            lock (_lock)
            {
                _activeSnapshot = snapshot;
            }
            System.Diagnostics.Debug.WriteLine("[AdBlocker] Background rule parsing completed. Swap successful.");

            // 4. Start background updates check
            _ = Task.Run(UpdateRulesFromWebAsync);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error in background rule parsing: {ex.Message}");
        }
    }

    private static void ParseRulesFile(string filePath, AdBlockSnapshot snapshot)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("!"))
                    continue;

                // Check cosmetic rule
                int idx = trimmed.IndexOf("##");
                int excIdx = trimmed.IndexOf("#@#");
                bool isCosmeticException = false;
                int splitIdx = -1;
                int separatorLen = 0;

                if (excIdx >= 0 && (idx == -1 || excIdx < idx))
                {
                    isCosmeticException = true;
                    splitIdx = excIdx;
                    separatorLen = 3;
                }
                else if (idx >= 0)
                {
                    splitIdx = idx;
                    separatorLen = 2;
                }

                if (splitIdx >= 0)
                {
                    string domainsPart = trimmed.Substring(0, splitIdx).Trim();
                    string selector = trimmed.Substring(splitIdx + separatorLen).Trim();
                    if (!string.IsNullOrEmpty(selector))
                    {
                        ParseCosmeticRule(domainsPart, selector, isCosmeticException, snapshot);
                    }
                    continue;
                }

                // Parse network rule
                bool isException = trimmed.StartsWith("@@");
                string ruleText = isException ? trimmed.Substring(2) : trimmed;
                ParseNetworkRule(ruleText, isException, snapshot);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error parsing rules file {filePath}: {ex.Message}");
        }
    }

    private static void ParseCosmeticRule(string domainsPart, string selector, bool isException, AdBlockSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(domainsPart))
        {
            if (!isException && snapshot.GlobalCosmeticRules.Count < 5000)
            {
                snapshot.GlobalCosmeticRules.Add(selector);
            }
            return;
        }

        var domains = domainsPart.Split(',');
        foreach (var dom in domains)
        {
            var cleanDom = dom.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(cleanDom)) continue;

            bool isExclusion = cleanDom.StartsWith("~");
            string d = isExclusion ? cleanDom.Substring(1) : cleanDom;

            if (isExclusion || isException)
            {
                if (!snapshot.DomainCosmeticExceptions.TryGetValue(d, out var list))
                {
                    list = new List<string>();
                    snapshot.DomainCosmeticExceptions[d] = list;
                }
                list.Add(selector);
            }
            else
            {
                if (!snapshot.DomainCosmeticRules.TryGetValue(d, out var list))
                {
                    list = new List<string>();
                    snapshot.DomainCosmeticRules[d] = list;
                }
                list.Add(selector);
            }
        }
    }

    private static void ParseNetworkRule(string ruleText, bool isException, AdBlockSnapshot snapshot)
    {
        string patternPart = ruleText;
        string optionsPart = string.Empty;

        int dollarIdx = ruleText.LastIndexOf('$');
        if (dollarIdx > 0)
        {
            patternPart = ruleText.Substring(0, dollarIdx);
            optionsPart = ruleText.Substring(dollarIdx + 1);
        }

        if (string.IsNullOrEmpty(patternPart)) return;

        var rule = new AdblockRule
        {
            RawRule = ruleText,
            IsException = isException
        };

        string pattern = patternPart;
        if (pattern.StartsWith("||"))
        {
            rule.IsDomainAnchor = true;
            pattern = pattern.Substring(2);
        }
        else if (pattern.StartsWith("|"))
        {
            rule.IsStartAnchor = true;
            pattern = pattern.Substring(1);
        }

        if (pattern.EndsWith("|"))
        {
            rule.IsEndAnchor = true;
            pattern = pattern.Substring(0, pattern.Length - 1);
        }

        rule.Pattern = pattern;
        rule.ResourceTypeMask = (int)AdBlockResourceType.All;

        if (!string.IsNullOrEmpty(optionsPart))
        {
            var options = optionsPart.Split(',');
            bool hasTypeOption = false;
            int typeMask = 0;
            int invertTypeMask = 0;

            foreach (var opt in options)
            {
                var cleanOpt = opt.Trim().ToLowerInvariant();
                if (cleanOpt == "third-party")
                {
                    rule.ThirdPartyOnly = true;
                }
                else if (cleanOpt == "~third-party")
                {
                    rule.FirstPartyOnly = true;
                }
                else if (cleanOpt.StartsWith("domain="))
                {
                    var domainsList = cleanOpt.Substring(7).Split('|');
                    rule.AllowedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    rule.BlockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var dom in domainsList)
                    {
                        if (dom.StartsWith("~"))
                        {
                            rule.BlockedDomains.Add(dom.Substring(1));
                        }
                        else
                        {
                            rule.AllowedDomains.Add(dom);
                        }
                    }
                }
                else
                {
                    bool invert = cleanOpt.StartsWith("~");
                    string typeName = invert ? cleanOpt.Substring(1) : cleanOpt;

                    AdBlockResourceType typeVal = typeName switch
                    {
                        "script" => AdBlockResourceType.Script,
                        "image" => AdBlockResourceType.Image,
                        "stylesheet" => AdBlockResourceType.Stylesheet,
                        "xmlhttprequest" => AdBlockResourceType.XmlHttpRequest,
                        "xhr" => AdBlockResourceType.XmlHttpRequest,
                        "subdocument" => AdBlockResourceType.Subdocument,
                        "subdoc" => AdBlockResourceType.Subdocument,
                        "ping" => AdBlockResourceType.Ping,
                        "media" => AdBlockResourceType.Media,
                        "font" => AdBlockResourceType.Font,
                        "other" => AdBlockResourceType.Other,
                        _ => AdBlockResourceType.None
                    };

                    if (typeVal != AdBlockResourceType.None)
                    {
                        if (invert)
                        {
                            invertTypeMask |= (int)typeVal;
                        }
                        else
                        {
                            hasTypeOption = true;
                            typeMask |= (int)typeVal;
                        }
                    }
                }
            }

            if (hasTypeOption)
            {
                rule.ResourceTypeMask = typeMask;
            }
            if (invertTypeMask != 0)
            {
                rule.ResourceTypeMask &= ~invertTypeMask;
            }
        }

        string? token = GetRuleToken(rule.Pattern);
        rule.FastToken = token;

        if (token != null)
        {
            var dict = isException ? snapshot.ExceptionRulesByToken : snapshot.BlockingRulesByToken;
            if (!dict.TryGetValue(token, out var list))
            {
                list = new List<AdblockRule>();
                dict[token] = list;
            }
            list.Add(rule);
        }
        else
        {
            var list = isException ? snapshot.UniversalExceptionRules : snapshot.UniversalBlockingRules;
            list.Add(rule);
        }
    }


    private static string? GetRuleToken(string pattern)
    {
        var words = pattern.Split(new[] { '|', '*', '^', '.', '/', '?', '&', '=', '_', '-', ':', '%' }, StringSplitOptions.RemoveEmptyEntries);
        string? bestToken = null;

        foreach (var word in words)
        {
            if (word.Length >= 3 && word.All(char.IsLetterOrDigit))
            {
                string cleanWord = word.ToLowerInvariant();
                if (!BadTokens.Contains(cleanWord))
                {
                    return cleanWord;
                }
                bestToken ??= cleanWord;
            }
        }

        return bestToken;
    }

    public static bool IsUrlBlocked(string url)
    {
        try
        {
            var uri = new Uri(url);
            return IsUrlBlocked(url, AdBlockResourceType.Other, uri.Host, false);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsUrlBlocked(string url, AdBlockResourceType resourceType, string documentHost, bool isThirdParty)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsDomainWhitelisted(documentHost))
        {
            return false;
        }

        try
        {
            var uri = new Uri(url);
            string requestHost = uri.Host;

            // Extract tokens from the request URL
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int currentLen = 0;
            int start = -1;
            for (int i = 0; i < url.Length; i++)
            {
                char c = url[i];
                if (char.IsLetterOrDigit(c))
                {
                    if (currentLen == 0) start = i;
                    currentLen++;
                }
                else
                {
                    if (currentLen >= 3)
                    {
                        string tokenStr = url.Substring(start, currentLen);
                        if (!BadTokens.Contains(tokenStr))
                        {
                            tokens.Add(tokenStr);
                        }
                    }
                    currentLen = 0;
                }
            }
            if (currentLen >= 3)
            {
                string tokenStr = url.Substring(start, currentLen);
                if (!BadTokens.Contains(tokenStr))
                {
                    tokens.Add(tokenStr);
                }
            }

            var snapshot = _activeSnapshot;

            // 1. Check exceptions
            foreach (var rule in snapshot.UniversalExceptionRules)
            {
                if (rule.Matches(url, requestHost, resourceType, documentHost, isThirdParty))
                {
                    return false;
                }
            }

            foreach (var tok in tokens)
            {
                if (snapshot.ExceptionRulesByToken.TryGetValue(tok, out var list))
                {
                    foreach (var rule in list)
                    {
                        if (rule.Matches(url, requestHost, resourceType, documentHost, isThirdParty))
                        {
                            return false;
                        }
                    }
                }
            }

            // 2. Check blocking rules
            foreach (var rule in snapshot.UniversalBlockingRules)
            {
                if (rule.Matches(url, requestHost, resourceType, documentHost, isThirdParty))
                {
                    return true;
                }
            }

            foreach (var tok in tokens)
            {
                if (snapshot.BlockingRulesByToken.TryGetValue(tok, out var list))
                {
                    foreach (var rule in list)
                    {
                        if (rule.Matches(url, requestHost, resourceType, documentHost, isThirdParty))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error checking URL: {ex.Message}");
        }

        return false;
    }

    public static string GetCosmeticCss()
    {
        var snapshot = _activeSnapshot;
        if (snapshot.GlobalCosmeticRules.Count == 0) return string.Empty;
        return string.Join(", ", snapshot.GlobalCosmeticRules);
    }

    public static string GetCosmeticCssForDomain(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return string.Empty;
        domain = domain.ToLowerInvariant().Trim();

        var snapshot = _activeSnapshot;
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = domain;
        while (!string.IsNullOrEmpty(current))
        {
            if (snapshot.DomainCosmeticExceptions.TryGetValue(current, out var list))
            {
                foreach (var sel in list) exclusions.Add(sel);
            }
            int dot = current.IndexOf('.');
            if (dot == -1) break;
            current = current.Substring(dot + 1);
        }

        var activeSelectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Add global rules not excluded
        foreach (var sel in snapshot.GlobalCosmeticRules)
        {
            if (!exclusions.Contains(sel))
            {
                activeSelectors.Add(sel);
            }
        }

        // 2. Add domain-specific rules matching this domain or parents
        current = domain;
        while (!string.IsNullOrEmpty(current))
        {
            if (snapshot.DomainCosmeticRules.TryGetValue(current, out var list))
            {
                foreach (var sel in list)
                {
                    if (!exclusions.Contains(sel))
                    {
                        activeSelectors.Add(sel);
                    }
                }
            }
            int dot = current.IndexOf('.');
            if (dot == -1) break;
            current = current.Substring(dot + 1);
        }

        if (activeSelectors.Count == 0) return string.Empty;

        return string.Join(", ", activeSelectors) + " { display: none !important; }";
    }

    private static async Task UpdateRulesFromWebAsync()
    {
        try
        {
            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }

            System.Diagnostics.Debug.WriteLine("[AdBlocker] Checking for updates on EasyList and EasyPrivacy...");
            
            bool updated = false;

            var listTask = DownloadFileAsync(EasyListUrl, EasyListPath);
            var privacyTask = DownloadFileAsync(EasyPrivacyUrl, EasyPrivacyPath);

            await Task.WhenAll(listTask, privacyTask);

            if (listTask.Result || privacyTask.Result)
            {
                updated = true;
            }

            if (updated)
            {
                System.Diagnostics.Debug.WriteLine("[AdBlocker] Rules updated from web, reloading rules in background...");
                _ = Task.Run(() => LoadFullRulesBackground());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Failed to update rules from web: {ex.Message}");
        }
    }

    private static async Task<bool> DownloadFileAsync(string url, string localPath)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(content))
                {
                    await File.WriteAllTextAsync(localPath, content);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error downloading {url}: {ex.Message}");
        }
        return false;
    }

    private static void LoadCustomBlocklist(AdBlockSnapshot snapshot)
    {
        try
        {
            if (!File.Exists(CustomBlocklistPath)) return;

            string json = File.ReadAllText(CustomBlocklistPath);
            var entries = JsonSerializer.Deserialize<CustomBlocklist>(json);
            if (entries == null) return;

            foreach (var domain in entries.Domains)
            {
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    ParseNetworkRule("||" + domain.Trim(), false, snapshot);
                }
            }
            foreach (var path in entries.Paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    ParseNetworkRule(path.Trim(), false, snapshot);
                }
            }
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Loaded {entries.Domains.Count} custom domains and {entries.Paths.Count} custom paths.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error loading custom blocklist: {ex.Message}");
        }
    }

    private static void LoadWhitelist()
    {
        try
        {
            if (!File.Exists(WhitelistPath))
            {
                lock (_lock)
                {
                    _whitelistedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                SaveWhitelist();
                return;
            }
            string json = File.ReadAllText(WhitelistPath);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list != null)
            {
                lock (_lock)
                {
                    _whitelistedDomains = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error loading whitelist: {ex.Message}");
        }
    }

    public static void SaveWhitelist()
    {
        try
        {
            List<string> list;
            lock (_lock)
            {
                list = _whitelistedDomains.ToList();
            }
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(WhitelistPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdBlocker] Error saving whitelist: {ex.Message}");
        }
    }

    public static bool IsDomainWhitelisted(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return false;
        lock (_lock)
        {
            string current = domain.ToLowerInvariant().Trim();
            while (!string.IsNullOrEmpty(current))
            {
                if (_whitelistedDomains.Contains(current))
                    return true;
                int dot = current.IndexOf('.');
                if (dot == -1) break;
                current = current.Substring(dot + 1);
            }
            return false;
        }
    }

    public static void ToggleDomainWhitelist(string domain, bool whitelist)
    {
        if (string.IsNullOrEmpty(domain)) return;
        lock (_lock)
        {
            string d = domain.ToLowerInvariant().Trim();
            if (whitelist)
                _whitelistedDomains.Add(d);
            else
                _whitelistedDomains.Remove(d);
        }
        SaveWhitelist();
    }

    private class CustomBlocklist
    {
        public List<string> Domains { get; set; } = new();
        public List<string> Paths { get; set; } = new();
    }
}
