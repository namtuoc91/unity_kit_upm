using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Raccoon.Localization
{
    /// <summary>
    /// Supported languages in the game.
    /// Add new languages here as needed.
    /// </summary>
    public enum Language
    {
        English,
        Vietnamese,
        French,
        German,
        Spanish,
        Japanese,
        Korean,
        ChineseSimplified,
        ChineseTraditional,
        Hindi,
    }

    /// <summary>
    /// Singleton LocalizationManager for Unity.
    /// Loads CSV/JSON locale files from Resources and provides key-based text lookup.
    ///
    /// SETUP:
    ///   1. Place locale files in: Resources/Localization/
    ///      - CSV format:  en.csv, vi.csv, fr.csv, ...
    ///      - JSON format: en.json, vi.json, fr.json, ...
    ///   2. Attach LocalizationManager to a persistent GameObject (or let it auto-create).
    ///   3. Call LocalizationManager.Instance.SetLanguage(Language.Vietnamese) to switch.
    ///   4. Subscribe to OnLanguageChanged to refresh UI text.
    ///
    /// SYSTEM LOCALE:
    ///   - Enable "Use System Language On First Run" in the Inspector to auto-detect on first run.
    ///   - Call SetLanguageToSystemLocale() at any time to snap back to the OS language.
    ///   - If the system locale has no matching Language enum entry, falls back to defaultLanguage.
    ///
    /// STARTUP PRIORITY:
    ///   1. Saved PlayerPrefs value (user previously chose a language).
    ///   2. System locale  — only when useSystemLanguageOnFirstRun = true AND no saved prefs.
    ///   3. defaultLanguage inspector field.
    ///
    /// CSV FORMAT (first column = key):
    ///   key,value
    ///   MENU_PLAY,Play
    ///   MENU_QUIT,Quit
    ///
    /// JSON FORMAT:
    ///   { "MENU_PLAY": "Play", "MENU_QUIT": "Quit" }
    /// </summary>
    public class LocalizationManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────────

        private static LocalizationManager _instance;

        public static LocalizationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[LocalizationManager]");
                    _instance = go.AddComponent<LocalizationManager>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        // ─── Events ───────────────────────────────────────────────────────────────

        /// <summary>Fired whenever the active language changes.</summary>
        public static event Action<Language> OnLanguageChanged;

        // ─── Config ───────────────────────────────────────────────────────────────

        [Header("System Locale")]
        [Tooltip("On first run (no saved PlayerPrefs), automatically detect the OS/device " +
                 "language and use it instead of defaultLanguage.")]
        [SerializeField]
        private bool useSystemLanguageOnFirstRun = true;

        [Tooltip("Fallback language used when:\n" +
                 "  (a) useSystemLanguageOnFirstRun is false\n" +
                 "  (b) no PlayerPrefs value is saved\n" +
                 "  (c) the detected system locale has no matching Language entry.")]
        [SerializeField]
        private Language defaultLanguage = Language.English;

        [Header("Settings")] [Tooltip("File format to load locale data from.")] [SerializeField]
        private LocaleFileFormat fileFormat = LocaleFileFormat.CSV;

        [Tooltip("Path inside Resources/ where locale files live (no trailing slash).")] [SerializeField]
        private string resourcesPath = "Localization";

        [Tooltip("PlayerPrefs key used to persist the selected language.")] [SerializeField]
        private string playerPrefsKey = "SelectedLanguage";

        [Tooltip("String returned when a key is missing. Use {0} for the key name.")] [SerializeField]
        private string missingKeyFormat = "[MISSING:{0}]";

        [Tooltip("Log a warning every time a missing key is accessed.")] [SerializeField]
        private bool warnOnMissingKey = true;

        // ─── State ────────────────────────────────────────────────────────────────

        private Language _currentLanguage;
        private Dictionary<string, string> _localizedStrings = new();
        private readonly Dictionary<Language, Dictionary<string, string>> _cache = new();

        /// <summary>The language currently active.</summary>
        public Language CurrentLanguage => _currentLanguage;

        /// <summary>
        /// The OS/device language mapped to a Language enum value.
        /// Returns null if the system locale has no matching entry.
        /// </summary>
        public Language? DetectedSystemLanguage => SystemLocaleToLanguage();

        // ─── File format enum ─────────────────────────────────────────────────────

        public enum LocaleFileFormat
        {
            CSV,
            JSON
        }

        // ─── Unity lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            _currentLanguage = ResolveStartupLanguage();
            LoadLanguage(_currentLanguage);
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Switch to a different language. Fires OnLanguageChanged on success.
        /// </summary>
        public void SetLanguage(Language language)
        {
            if (language == _currentLanguage && _localizedStrings.Count > 0) return;

            LoadLanguage(language);
            _currentLanguage = language;

            PlayerPrefs.SetString(playerPrefsKey, language.ToString());
            PlayerPrefs.Save();

            OnLanguageChanged?.Invoke(_currentLanguage);
        }

        /// <summary>
        /// Detects the OS/device locale and switches to the matching Language.
        /// If no match is found, falls back to <see cref="defaultLanguage"/>.
        /// Always saves the result to PlayerPrefs.
        /// </summary>
        public void SetLanguageToSystemLocale()
        {
            Language target = SystemLocaleToLanguage() ?? defaultLanguage;
            Debug.Log($"[Localization] SetLanguageToSystemLocale → {target} " +
                      $"(Unity: {Application.systemLanguage}, " +
                      $"CultureInfo: {CultureInfo.CurrentUICulture.Name})");
            SetLanguage(target);
        }

        /// <summary>
        /// Returns the localized string for <paramref name="key"/>.
        /// Supports optional string.Format args: Get("SCORE_FMT", score)
        /// </summary>
        public string Get(string key, params object[] args)
        {
            if (_localizedStrings.TryGetValue(key, out string value))
                return args.Length > 0 ? string.Format(value, args) : value;

            if (warnOnMissingKey)
                Debug.LogWarning($"[Localization] Missing key '{key}' for language '{_currentLanguage}'.");

            return string.Format(missingKeyFormat, key);
        }

        /// <summary>Shorthand alias for Get().</summary>
        public string T(string key, params object[] args) => Get(key, args);

        /// <summary>Returns true if the key exists in the current locale.</summary>
        public bool HasKey(string key) => _localizedStrings.ContainsKey(key);

        /// <summary>Returns all strings in the current locale (useful for editor tools).</summary>
        public IReadOnlyDictionary<string, string> GetAllStrings() => _localizedStrings;

        /// <summary>
        /// Clears the in-memory cache so files are re-read on next load.
        /// Useful during development when locale files change at runtime.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
            LoadLanguage(_currentLanguage);
        }

        // ─── Startup resolution ───────────────────────────────────────────────────

        /// <summary>
        /// Determines which language to load at startup.
        ///
        /// Priority:
        ///   1. Saved PlayerPrefs  (user explicitly chose a language before).
        ///   2. System locale      (first run + useSystemLanguageOnFirstRun = true).
        ///   3. defaultLanguage    (ultimate fallback).
        /// </summary>
        private Language ResolveStartupLanguage()
        {
            // 1 ── Restore previously saved preference ────────────────────────────
            if (PlayerPrefs.HasKey(playerPrefsKey))
            {
                if (Enum.TryParse(PlayerPrefs.GetString(playerPrefsKey), out Language saved))
                {
                    Debug.Log($"[Localization] Restored saved language: {saved}");
                    return saved;
                }
            }

            // 2 ── First run: auto-detect system locale ───────────────────────────
            if (useSystemLanguageOnFirstRun)
            {
                Language? detected = SystemLocaleToLanguage();
                if (detected.HasValue)
                {
                    Debug.Log($"[Localization] First run — system locale detected: {detected.Value}");
                    // Persist immediately so subsequent launches skip detection
                    PlayerPrefs.SetString(playerPrefsKey, detected.Value.ToString());
                    PlayerPrefs.Save();
                    return detected.Value;
                }

                Debug.Log($"[Localization] First run — system locale '{Application.systemLanguage}' " +
                          $"not supported, falling back to: {defaultLanguage}");
            }

            // 3 ── Hard default ────────────────────────────────────────────────────
            return defaultLanguage;
        }

        // ─── System locale detection ──────────────────────────────────────────────

        /// <summary>
        /// Maps the current device/OS locale to a <see cref="Language"/> enum value.
        ///
        /// Strategy:
        ///   1. CultureInfo.CurrentUICulture (BCP-47) — gives zh-CN vs zh-TW precision.
        ///   2. Unity Application.systemLanguage     — reliable fallback on consoles/mobile.
        ///
        /// Returns null when neither source maps to a supported Language.
        /// </summary>
        private Language? SystemLocaleToLanguage()
        {
            // Step 1 — CultureInfo (most precise) ---------------------------------
            try
            {
                string culture = CultureInfo.CurrentUICulture.Name; // e.g. "zh-TW", "vi-VN"
                Language? fromCulture = CultureNameToLanguage(culture);
                if (fromCulture.HasValue) return fromCulture;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Localization] CultureInfo lookup failed: {ex.Message}");
            }

            // Step 2 — Unity SystemLanguage (safe fallback) -----------------------
            return UnitySystemLanguageToLanguage(Application.systemLanguage);
        }

        /// <summary>
        /// Maps a BCP-47 culture name string to a Language enum value.
        /// Handles exact codes and common regional variants.
        /// Returns null for unsupported locales.
        /// </summary>
        private static Language? CultureNameToLanguage(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            return name.ToLowerInvariant() switch
            {
                // Chinese — must come before generic "zh" check
                "zh-cn" or "zh-hans" or "zh-sg" => Language.ChineseSimplified,
                "zh-tw" or "zh-hant" or "zh-hk" or "zh-mo" => Language.ChineseTraditional,

                // East Asian
                "ja" or "ja-jp" => Language.Japanese,
                "ko" or "ko-kr" => Language.Korean,

                // South-East Asian
                "vi" or "vi-vn" => Language.Vietnamese,

                // European
                "fr" or "fr-fr" or "fr-be" or "fr-ch" or
                    "fr-ca" or "fr-lu" or "fr-mc" => Language.French,

                "de" or "de-de" or "de-at" or "de-ch" or
                    "de-lu" or "de-li" => Language.German,

                "es" or "es-es" or "es-mx" or "es-ar" or
                    "es-co" or "es-cl" or "es-pe" or "es-ve" or
                    "es-us" or "es-419" => Language.Spanish,

                "en" or "en-us" or "en-gb" or "en-au" or
                    "en-ca" or "en-nz" or "en-in" or "en-sg" or
                    "en-za" or "en-ie" => Language.English,

                // Indic
                "hi" or "hi-in" => Language.Hindi,

                // Prefix fallback for unrecognised regional variants (e.g. "fr-ma" → French)
                _ => PrefixFallback(name),
            };
        }

        /// <summary>
        /// Last-resort prefix match: "fr-XX" → French, "de-XX" → German, etc.
        /// Returns null if no prefix matches.
        /// </summary>
        private static Language? PrefixFallback(string name)
        {
            if (name.StartsWith("zh-hans", StringComparison.OrdinalIgnoreCase)) return Language.ChineseSimplified;
            if (name.StartsWith("zh-hant", StringComparison.OrdinalIgnoreCase)) return Language.ChineseTraditional;
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return Language.ChineseSimplified;
            if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return Language.Japanese;
            if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return Language.Korean;
            if (name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)) return Language.Vietnamese;
            if (name.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return Language.French;
            if (name.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return Language.German;
            if (name.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return Language.Spanish;
            if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return Language.English;
            if (name.StartsWith("hi", StringComparison.OrdinalIgnoreCase)) return Language.Hindi;
            return null;
        }

        /// <summary>
        /// Maps Unity's <see cref="SystemLanguage"/> enum to a Language enum value.
        /// Returns null for languages not present in the Language enum.
        /// </summary>
        private static Language? UnitySystemLanguageToLanguage(SystemLanguage lang) => lang switch
        {
            SystemLanguage.English => Language.English,
            SystemLanguage.Vietnamese => Language.Vietnamese,
            SystemLanguage.French => Language.French,
            SystemLanguage.German => Language.German,
            SystemLanguage.Spanish => Language.Spanish,
            SystemLanguage.Japanese => Language.Japanese,
            SystemLanguage.Korean => Language.Korean,
            SystemLanguage.ChineseSimplified => Language.ChineseSimplified,
            SystemLanguage.ChineseTraditional => Language.ChineseTraditional,
            SystemLanguage.Chinese => Language.ChineseSimplified, // ambiguous — prefer simplified
            SystemLanguage.Hindi => Language.Hindi,
            _ => null,
        };

        // ─── Loading ──────────────────────────────────────────────────────────────

        private void LoadLanguage(Language language)
        {
            if (_cache.TryGetValue(language, out var cached))
            {
                _localizedStrings = cached;
                return;
            }

            string fileName = LanguageToFileName(language);
            string fullPath = $"{resourcesPath}/{fileName}";

            TextAsset asset = Resources.Load<TextAsset>(fullPath);
            if (asset == null)
            {
                Debug.LogWarning($"[Localization] Could not load file: Resources/{fullPath}. " +
                                 $"Falling back to '{defaultLanguage}'.");

                // If this IS already the default language and it's also missing, give up
                if (language == defaultLanguage)
                {
                    Debug.LogError($"[Localization] Default language file also missing: " +
                                   $"Resources/{resourcesPath}/{LanguageToFileName(defaultLanguage)}");
                    _localizedStrings = new Dictionary<string, string>();
                    return;
                }

                // Fall back: load default language instead
                _currentLanguage = defaultLanguage;
                LoadLanguage(defaultLanguage);
                return;
            }

            _localizedStrings = fileFormat == LocaleFileFormat.CSV
                ? ParseCSV(asset.text)
                : ParseJSON(asset.text);

            _cache[language] = _localizedStrings;
            Debug.Log($"[Localization] Loaded '{language}' ({_localizedStrings.Count} keys) from {fullPath}");
        }

        // ─── Parsers ──────────────────────────────────────────────────────────────

        private static Dictionary<string, string> ParseCSV(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool firstLine = true;

            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var (key, value) = SplitFirstCSVColumns(line);

                if (firstLine && key.Equals("key", StringComparison.OrdinalIgnoreCase))
                {
                    firstLine = false;
                    continue;
                }

                firstLine = false;

                if (string.IsNullOrEmpty(key)) continue;
                result[key.Trim()] = UnescapeCSVValue(value);
            }

            return result;
        }

        private static (string key, string value) SplitFirstCSVColumns(string line)
        {
            if (line.Length == 0) return ("", "");
            int ci = line.IndexOf(',');
            return ci < 0 ? (line, "") : (line[..ci], line[(ci + 1)..]);
        }

        private static string UnescapeCSVValue(string raw)
        {
            raw = raw.Trim();
            if (raw.StartsWith("\"") && raw.EndsWith("\""))
                raw = raw[1..^1].Replace("\"\"", "\"");
            return raw.Replace("\\n", "\n");
        }

        private static Dictionary<string, string> ParseJSON(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            json = json.Trim().TrimStart('{').TrimEnd('}');

            int i = 0;
            while (i < json.Length)
            {
                int ks = json.IndexOf('"', i);
                if (ks < 0) break;
                int ke = FindClosingQuote(json, ks + 1);
                if (ke < 0) break;
                string key = json.Substring(ks + 1, ke - ks - 1);

                int colon = json.IndexOf(':', ke + 1);
                if (colon < 0) break;
                int vs = json.IndexOf('"', colon + 1);
                if (vs < 0) break;
                int ve = FindClosingQuote(json, vs + 1);
                if (ve < 0) break;
                string value = json.Substring(vs + 1, ve - vs - 1);

                result[key] = UnescapeJSON(value);
                i = ve + 1;
            }

            return result;
        }

        private static int FindClosingQuote(string s, int start)
        {
            for (int i = start; i < s.Length; i++)
            {
                if (s[i] == '\\')
                {
                    i++;
                    continue;
                }

                if (s[i] == '"') return i;
            }

            return -1;
        }

        private static string UnescapeJSON(string s)
            => s.Replace("\\n", "\n")
                .Replace("\\t", "\t")
                .Replace("\\r", "\r")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");

        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Locale file name (without extension) for a language, e.g. Vietnamese → "vi".</summary>
        public static string LanguageToFileName(Language lang) => lang switch
        {
            Language.English => "en",
            Language.Vietnamese => "vi",
            Language.French => "fr",
            Language.German => "de",
            Language.Spanish => "es",
            Language.Japanese => "ja",
            Language.Korean => "ko",
            Language.ChineseSimplified => "zh-CN",
            Language.ChineseTraditional => "zh-TW",
            Language.Hindi => "hi",
            _ => "en",
        };
    }
}