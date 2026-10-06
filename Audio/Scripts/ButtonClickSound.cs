using UnityEngine;
using UnityEngine.UI;

namespace Raccoon.Audio
{
    /// <summary>
    /// Add to a Button to play a click sound through GameAudio when it is clicked.
    /// Leave customSound empty to use GameAudio's default clickSound.
    /// </summary>

    [RequireComponent(typeof(Button))]
    public class ButtonClickSound : MonoBehaviour
    {
        [Tooltip("Sound id from the AudioLibrary, used instead of customSound when set")]
        [SerializeField] private string soundId;
        [SerializeField] private AudioClip customSound;
        [SerializeField, Range(0f, 1f)] private float volume = GameAudio.DEFAULT_CLICK_VOLUME;

        private Button button;

        void Awake()
        {
            button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            button.onClick.AddListener(PlaySound);
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(PlaySound);
        }

        private void PlaySound()
        {
            if (!string.IsNullOrEmpty(soundId))
                GameAudio.SFX(soundId);
            else
                GameAudio.ClickButton(customSound, volume);
        }
    }

}
