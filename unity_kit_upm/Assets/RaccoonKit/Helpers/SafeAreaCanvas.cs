using UnityEngine;

namespace Raccoon.Helpers
{
    public class SafeAreaCanvas : MonoBehaviour
    {
        // Reference to your UI RectTransform
        [System.NonSerialized]
        public RectTransform uiPanel;

        static bool _isCalculated;
        static Vector2 _cachedAnchorMin;
        static Vector2 _cachedAnchorMax;

        void Start()
        {
            ApplySafeArea();
        }

        void ApplySafeArea()
        {
            //Debug.Log("check canvas helper " + gameObject.name);
            if (uiPanel == null)
                uiPanel = gameObject.GetComponent<RectTransform>();

            if (uiPanel == null) return;

            if (!_isCalculated)
            {
                Rect safeArea = Screen.safeArea;

                // Convert screen coordinates to canvas coordinates
                Vector2 offsetMin = safeArea.position;
                Vector2 offsetMax = safeArea.position + safeArea.size;

                offsetMin.x /= Screen.width;
                offsetMin.y /= Screen.height;
                offsetMax.x /= Screen.width;
                offsetMax.y /= Screen.height;

                _cachedAnchorMin = offsetMin;
                _cachedAnchorMax = offsetMax;
                _isCalculated = true;
            }

            // Apply the cached safe area insets to the UI element
            uiPanel.anchorMin = _cachedAnchorMin;
            uiPanel.anchorMax = _cachedAnchorMax;
        }
    }
}