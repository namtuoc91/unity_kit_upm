// Singleton that holds font references for every language.
// Use it anywhere you set text in code so the correct font
// is always applied alongside the localized string.
//
// USAGE IN CODE
// ─────────────
// // TMP
// myTMPText.font = FontManager.Instance.GetTMP();
// myTMPText.text = LocalizationManager.Instance.Get("MY_KEY");
//
// // Legacy Text
// myLegacyText.font = FontManager.Instance.GetLegacy();
// myLegacyText.text = LocalizationManager.Instance.Get("MY_KEY");
//
// // One-liner helpers (set font + text together)
// FontManager.Instance.SetText(myTMPText,    "MY_KEY");
// FontManager.Instance.SetText(myLegacyText, "MY_KEY");
// FontManager.Instance.SetText(myTMPText,    "SCORE_KEY", score);   // with format args
//
// SETUP
// ─────
// 1. Add FontManager to a persistent GameObject (or let it auto-create).
// 2. Assign font assets in the Inspector.
// 3. Call FontManager.Instance.SetText() wherever you currently do
//    myText.text = LocalizationManager.Instance.Get("KEY");

#if USING_TMP
using TMPro;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace Raccoon.Localization
{
    public class FontManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────────

        private static FontManager _instance;

        public static FontManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[FontManager]");
                    _instance = go.AddComponent<FontManager>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

#if USING_TMP
        // ─── TMP Font slots ───────────────────────────────────────────────────────

        [Header("TMP Fonts (TextMeshProUGUI / TextMeshPro)")]
        [Tooltip("Default TMP font for Latin scripts (EN, FR, DE, ES, VI…)")]
        [SerializeField]
        private TMP_FontAsset tmpDefault;

        [Tooltip("Devanagari TMP font — generate via Window → TMP → Font Asset Creator\n" +
                 "Unicode range: 0900-097F")]
        [SerializeField]
        private TMP_FontAsset tmpHindi;

        [SerializeField] private TMP_FontAsset tmpJapanese;
        [SerializeField] private TMP_FontAsset tmpKorean;
        [SerializeField] private TMP_FontAsset tmpChineseSimplified;
        [SerializeField] private TMP_FontAsset tmpChineseTraditional;
#endif

        // ─── Legacy Text font slots ───────────────────────────────────────────────

        [Header("Legacy Fonts (UnityEngine.UI.Text)")]
        [Tooltip("Default legacy font for Latin scripts.")]
        [SerializeField]
        private Font legacyDefault;

        [Tooltip("Devanagari legacy font — import .ttf and set Character to Unicode.")] [SerializeField]
        private Font legacyHindi;

        [SerializeField] private Font legacyJapanese;
        [SerializeField] private Font legacyKorean;
        [SerializeField] private Font legacyChineseSimplified;
        [SerializeField] private Font legacyChineseTraditional;

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
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Public API — font getters
        // ═════════════════════════════════════════════════════════════════════════

#if USING_TMP
        /// <summary>Returns the correct TMP font for the currently active language.</summary>
        public TMP_FontAsset GetTMP()
            => GetTMP(LocalizationManager.Instance.CurrentLanguage);

        /// <summary>Returns the correct TMP font for a specific language.</summary>
        public TMP_FontAsset GetTMP(Language language)
        {
            TMP_FontAsset f = language switch
            {
                Language.Hindi => tmpHindi,
                Language.Japanese => tmpJapanese,
                Language.Korean => tmpKorean,
                Language.ChineseSimplified => tmpChineseSimplified,
                Language.ChineseTraditional => tmpChineseTraditional,
                _ => tmpDefault,
            };
            return f != null ? f : tmpDefault;
        }
#endif

        /// <summary>Returns the correct legacy Font for the currently active language.</summary>
        public Font GetLegacy()
            => GetLegacy(LocalizationManager.Instance.CurrentLanguage);

        /// <summary>Returns the correct legacy Font for a specific language.</summary>
        public Font GetLegacy(Language language)
        {
            Font f = language switch
            {
                Language.Hindi => legacyHindi,
                Language.Japanese => legacyJapanese,
                Language.Korean => legacyKorean,
                Language.ChineseSimplified => legacyChineseSimplified,
                Language.ChineseTraditional => legacyChineseTraditional,
                _ => legacyDefault,
            };
            return f != null ? f : legacyDefault;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Public API — one-liner SetText helpers
        // ═════════════════════════════════════════════════════════════════════════

#if USING_TMP
        /// <summary>
        /// Sets the correct font AND localized text on a TMP_Text in one call.
        /// Replaces: myText.text = LocalizationManager.Instance.Get("KEY");
        /// </summary>
        public void SetText(TMP_Text target, string key, params object[] args)
        {
            if (target == null) return;
            target.font = GetTMP();
            target.text = LocalizationManager.Instance.Get(key, args);
        }
#endif

        /// <summary>
        /// Sets the correct font AND localized text on a legacy Text in one call.
        /// Replaces: myText.text = LocalizationManager.Instance.Get("KEY");
        /// </summary>
        public void SetText(Text target, string key, params object[] args)
        {
            if (target == null) return;
            target.font = GetLegacy();
            target.text = LocalizationManager.Instance.Get(key, args);
        }

#if USING_TMP
        /// <summary>
        /// Sets the correct font AND a raw (already-localized) string on a TMP_Text.
        /// Use when the string comes from game logic, not a localization key.
        /// e.g. FontManager.Instance.SetRawText(myText, playerName);
        /// </summary>
        public void SetRawText(TMP_Text target, string text)
        {
            if (target == null) return;
            target.font = GetTMP();
            target.text = text;
        }
#endif

        /// <summary>
        /// Sets the correct font AND a raw string on a legacy Text.
        /// </summary>
        public void SetRawText(Text target, string text)
        {
            if (target == null) return;
            target.font = GetLegacy();
            target.text = text;
        }
    }
}
