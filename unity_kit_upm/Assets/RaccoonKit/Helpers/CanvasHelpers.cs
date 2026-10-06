using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Raccoon.Helpers
{
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasScaler))]
    public class CanvasHelpers : MonoBehaviour
    {
        public enum Orientation_Type { Portrait, Landscape }

        public Orientation_Type orientation_Type = Orientation_Type.Landscape;
        public CanvasScaler canvasScaler;

        // Your design base ratio: 2048 / 970 = 2.11
        private const float BASE_RATIO = 2.11f;

        private int lastWidth, lastHeight;

        void Start()
        {
            if (canvasScaler == null)
                canvasScaler = GetComponent<CanvasScaler>();

            lastWidth = Screen.width;
            lastHeight = Screen.height;

            CheckWidthHeight();
        }

        void Update()
        {
            if (Screen.width != lastWidth || Screen.height != lastHeight)
            {
                lastWidth = Screen.width;
                lastHeight = Screen.height;
                CheckWidthHeight();
            }
        }

        void CheckWidthHeight()
        {
            if (canvasScaler == null) return;

            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            float currentRatio = (float)Screen.width / Screen.height;

            if (orientation_Type == Orientation_Type.Landscape)
            {
                // If current screen is wider than your base ratio, match height (1)
                // If narrower (like iPad 4:3), match width (0)
                canvasScaler.matchWidthOrHeight = (currentRatio > BASE_RATIO) ? 1f : 0f;
            }
            else
            {
                // Portrait: use inverse logic
                float invRatio = (float)Screen.height / Screen.width;
                canvasScaler.matchWidthOrHeight = (invRatio > (1f / BASE_RATIO)) ? 0f : 1f;
            }
        }
    }

}