// Attach alongside LocalizedText on any GameObject that needs a different
// font for specific languages (e.g. Hindi/Devanagari).
//
// Supports BOTH:
//   • TMP_Text  (TextMeshProUGUI / TextMeshPro)
//   • Text      (legacy UnityEngine.UI.Text)
//
// Assign font slots in the Inspector. Any language left null falls
// back to the respective default font cached from the component on Awake.

#if USING_TMP
using TMPro;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace Raccoon.Localization
{
    /// <summary>
    /// Swaps the font on a <see cref="TMP_Text"/> OR legacy <see cref="Text"/>
    /// component whenever the active language changes.
    /// Works alongside <see cref="LocalizedText"/> — attach both to the same GameObject.
    ///
    /// SETUP (TMP)
    /// ───────────
    /// 1. Window → TextMeshPro → Font Asset Creator
    /// 2. Source Font : your Devanagari .ttf  (e.g. Noto Sans Devanagari)
    /// 3. Character Set : Unicode Range (Hex) → 0900-097F
    /// 4. Generate → Save as NotoSansDevanagari_SDF.asset
    /// 5. Assign to the "Hindi Font (TMP)" slot below.
    ///
    /// SETUP (Legacy Text)
    /// ───────────────────
    /// 1. Import a Devanagari .ttf into Unity (e.g. Noto Sans Devanagari).
    /// 2. In the Font Import Settings set Character to "Unicode".
    /// 3. Assign to the "Hindi Font (Legacy)" slot below.
    /// </summary>
    public class LocalizedFont : MonoBehaviour
    {
#if USING_TMP
        // ── TMP font slots ────────────────────────────────────────────────────────

        [Header("TMP Fonts (TextMeshProUGUI / TextMeshPro)")]
        [Tooltip("Default TMP font for Latin languages (EN, FR, DE, ES…). " +
                 "Auto-cached from the TMP_Text component if left null.")]
        [SerializeField]
        private TMP_FontAsset tmpDefaultFont;

        [Tooltip("Devanagari TMP font for Hindi. Generate via TMP Font Asset Creator " +
                 "with Unicode range 0900-097F.")]
        [SerializeField]
        private TMP_FontAsset tmpHindiFont;

        [Tooltip("TMP font for Japanese. Leave null to use the default.")] [SerializeField]
        private TMP_FontAsset tmpJapaneseFont;

        [Tooltip("TMP font for Korean. Leave null to use the default.")] [SerializeField]
        private TMP_FontAsset tmpKoreanFont;

        [Tooltip("TMP font for Chinese Simplified. Leave null to use the default.")] [SerializeField]
        private TMP_FontAsset tmpChineseSimplifiedFont;

        [Tooltip("TMP font for Chinese Traditional. Leave null to use the default.")] [SerializeField]
        private TMP_FontAsset tmpChineseTraditionalFont;
#endif

        // ── Legacy Text font slots ────────────────────────────────────────────────

        [Header("Legacy Fonts (UnityEngine.UI.Text)")]
        [Tooltip("Default legacy font for Latin languages. " +
                 "Auto-cached from the Text component if left null.")]
        [SerializeField]
        private Font legacyDefaultFont;

        [Tooltip("Devanagari legacy font for Hindi. Import a .ttf and set " +
                 "Character to Unicode in its Import Settings.")]
        [SerializeField]
        private Font legacyHindiFont;

        [Tooltip("Legacy font for Japanese. Leave null to use the default.")] [SerializeField]
        private Font legacyJapaneseFont;

        [Tooltip("Legacy font for Korean. Leave null to use the default.")] [SerializeField]
        private Font legacyKoreanFont;

        [Tooltip("Legacy font for Chinese Simplified. Leave null to use the default.")] [SerializeField]
        private Font legacyChineseSimplifiedFont;

        [Tooltip("Legacy font for Chinese Traditional. Leave null to use the default.")] [SerializeField]
        private Font legacyChineseTraditionalFont;

        // ── Cached component references ───────────────────────────────────────────

#if USING_TMP
        private TMP_Text _tmpText;
#endif
        private Text _legacyText;

        // ─── Unity lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
#if USING_TMP
            _tmpText = GetComponent<TMP_Text>();
#endif
            _legacyText = GetComponent<Text>();

            // Auto-cache defaults from the component if not set in Inspector
#if USING_TMP
            if (_tmpText != null && tmpDefaultFont == null) tmpDefaultFont = _tmpText.font;
#endif
            if (_legacyText != null && legacyDefaultFont == null) legacyDefaultFont = _legacyText.font;
        }

        private void OnEnable()
        {
            LocalizationManager.OnLanguageChanged += ApplyFont;
            ApplyFont(LocalizationManager.Instance.CurrentLanguage);
        }

        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= ApplyFont;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Force a font refresh for the given language.</summary>
        public void ApplyFont(Language language)
        {
#if USING_TMP
            ApplyTMP(language);
#endif
            ApplyLegacy(language);
        }

        // ── Internal ──────────────────────────────────────────────────────────────

#if USING_TMP
        private void ApplyTMP(Language language)
        {
            if (_tmpText == null) return;

            TMP_FontAsset font = ResolveTMP(language);
            if (font != null && _tmpText.font != font)
                _tmpText.font = font;
        }
#endif

        private void ApplyLegacy(Language language)
        {
            if (_legacyText == null) return;

            Font font = ResolveLegacy(language);
            if (font != null && _legacyText.font != font)
                _legacyText.font = font;
        }

#if USING_TMP
        private TMP_FontAsset ResolveTMP(Language language)
        {
            TMP_FontAsset candidate = language switch
            {
                Language.Hindi => tmpHindiFont,
                Language.Japanese => tmpJapaneseFont,
                Language.Korean => tmpKoreanFont,
                Language.ChineseSimplified => tmpChineseSimplifiedFont,
                Language.ChineseTraditional => tmpChineseTraditionalFont,
                _ => tmpDefaultFont,
            };
            return candidate != null ? candidate : tmpDefaultFont;
        }
#endif

        private Font ResolveLegacy(Language language)
        {
            Font candidate = language switch
            {
                Language.Hindi => legacyHindiFont,
                Language.Japanese => legacyJapaneseFont,
                Language.Korean => legacyKoreanFont,
                Language.ChineseSimplified => legacyChineseSimplifiedFont,
                Language.ChineseTraditional => legacyChineseTraditionalFont,
                _ => legacyDefaultFont,
            };
            return candidate != null ? candidate : legacyDefaultFont;
        }
    }
}
