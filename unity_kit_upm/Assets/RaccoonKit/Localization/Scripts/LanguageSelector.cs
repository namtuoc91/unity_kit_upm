using System;
using System.Linq;
#if USING_TMP
using TMPro;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace Raccoon.Localization
{
#if USING_TMP
    using DropdownType = TMP_Dropdown;
#else
    using DropdownType = Dropdown;
#endif

    /// <summary>
    /// Wires a UI Dropdown (Legacy or TMP) to the LocalizationManager.
    /// Drop this on the same GameObject as your Dropdown component.
    ///
    /// The dropdown options are auto-populated from the Language enum.
    /// Selecting an option immediately switches the active language.
    /// </summary>
    [RequireComponent(typeof(DropdownType))] // TMP_Dropdown when USING_TMP is defined, else legacy Dropdown
    public class LanguageSelector : MonoBehaviour
    {
        private DropdownType _dropdown;
        private Language[] _languages;

        private void Awake()
        {
            _dropdown = GetComponent<DropdownType>();
            _languages = (Language[])Enum.GetValues(typeof(Language));
        }

        private void Start()
        {
            PopulateDropdown();
            _dropdown.onValueChanged.AddListener(OnDropdownChanged);
            LocalizationManager.OnLanguageChanged += SyncDropdownToLanguage;
            SyncDropdownToLanguage(LocalizationManager.Instance.CurrentLanguage);
        }

        private void OnDestroy()
        {
            _dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
            LocalizationManager.OnLanguageChanged -= SyncDropdownToLanguage;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private void PopulateDropdown()
        {
            _dropdown.ClearOptions();
            _dropdown.AddOptions(
                _languages.Select(l => new DropdownType.OptionData(LanguageDisplayName(l))).ToList()
            );
        }

        private void OnDropdownChanged(int index)
        {
            if (index >= 0 && index < _languages.Length)
                LocalizationManager.Instance.SetLanguage(_languages[index]);
        }

        private void SyncDropdownToLanguage(Language language)
        {
            int index = Array.IndexOf(_languages, language);
            if (index >= 0) _dropdown.SetValueWithoutNotify(index);
        }

        private static string LanguageDisplayName(Language lang) => lang switch
        {
            Language.English => "English",
            Language.Vietnamese => "Tiếng Việt",
            Language.French => "Français",
            Language.German => "Deutsch",
            Language.Spanish => "Español",
            Language.Japanese => "日本語",
            Language.Korean => "한국어",
            Language.ChineseSimplified => "简体中文",
            Language.ChineseTraditional => "繁體中文",
            _ => lang.ToString(),
        };
    }
}
