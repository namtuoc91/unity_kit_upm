using UnityEngine;
#if USING_TMP
using TMPro;
#endif
using UnityEngine.UI; // Legacy UI Text

namespace Raccoon.Localization
{
    /// <summary>
    /// Attach to any GameObject with a Text or TMP_Text component.
    /// Automatically updates the display string whenever the language changes.
    ///
    /// Supports format args embedded in the key via LocalizationArgs (optional).
    /// </summary>
    [DisallowMultipleComponent]
    public class LocalizedText : MonoBehaviour
    {
        [Header("Localization")] [Tooltip("The localization key to look up (e.g. MENU_PLAY).")] [SerializeField]
        private string key;

        [Tooltip("Optional format arguments inserted via string.Format. Leave empty if the value has no placeholders.")]
        [SerializeField]
        private string[] formatArgs;

        // Cached component references
#if USING_TMP
        private TMP_Text _tmpText;
#endif
        private Text _legacyText;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
#if USING_TMP
            _tmpText = GetComponent<TMP_Text>();
#endif
            _legacyText = GetComponent<Text>();
        }

        private void OnEnable()
        {
            LocalizationManager.OnLanguageChanged += OnLanguageChanged;
            Refresh();
        }

        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= OnLanguageChanged;
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>Change the localization key at runtime and refresh immediately.</summary>
        public void SetKey(string newKey, params string[] args)
        {
            key = newKey;
            formatArgs = args;
            Refresh();
        }

        /// <summary>Force a text refresh from the current locale data.</summary>
        public void Refresh()
        {
            if (LocalizationManager.Instance == null) return;
            if (string.IsNullOrEmpty(key)) return;

            string value = formatArgs is { Length: > 0 }
                ? LocalizationManager.Instance.Get(key, (object[])formatArgs)
                : LocalizationManager.Instance.Get(key);

#if USING_TMP
            if (_tmpText != null) _tmpText.text = value;
#endif
            if (_legacyText != null) _legacyText.text = value;
        }

        // ─── Handlers ─────────────────────────────────────────────────────────────

        private void OnLanguageChanged(Language _) => Refresh();

#if UNITY_EDITOR
        // Live-preview in the Editor without entering Play Mode
        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            Refresh();
        }
#endif
    }
}
