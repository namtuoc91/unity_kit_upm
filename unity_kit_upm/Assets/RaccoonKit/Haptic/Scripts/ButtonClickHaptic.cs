using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Raccoon.Haptic
{
    /// <summary>
    /// Add to a Button to play a haptic through GameHaptic when it is pressed.
    /// Plays on pointer down (like native iOS buttons) instead of onClick which fires on release,
    /// and on submit (keyboard/gamepad) since there is no pointer down in that case.
    /// </summary>

    [RequireComponent(typeof(Button))]
    public class ButtonClickHaptic : MonoBehaviour, IPointerDownHandler, ISubmitHandler
    {
        [SerializeField] private HapticType hapticType = HapticType.Light;

        private Button button;

        void Awake()
        {
            button = GetComponent<Button>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            //Same rule as Button: only the left mouse button (a touch is always reported as left)
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            PlayHaptic();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            PlayHaptic();
        }

        private void PlayHaptic()
        {
            //onClick already ignored non interactable buttons, pointer down doesn't so it is checked here
            if (!button.IsActive() || !button.IsInteractable())
                return;

            GameHaptic.Play(hapticType);
        }
    }

}
