using UnityEngine;

namespace Raccoon.Save
{
    /// <summary>
    /// Hidden DontDestroyOnLoad object created by GameSave on load, saves when the app goes to background.
    /// On iOS / Android OnApplicationQuit is usually not called (swipe kill, OS kill), pause is the reliable moment.
    /// </summary>
    [AddComponentMenu("")]
    internal class SaveLifecycle : MonoBehaviour
    {
        private static SaveLifecycle instance;

        private float autoSaveInterval;
        private bool saveOnFocusLost;
        private float nextAutoSaveTime;

        internal static void Create(SaveOptions options)
        {
            if (instance != null) return;
            GameObject go = new GameObject("[GameSave]") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SaveLifecycle>();
            instance.autoSaveInterval = options.AutoSaveInterval;
            instance.saveOnFocusLost = options.SaveOnFocusLost;
            instance.nextAutoSaveTime = Time.unscaledTime + options.AutoSaveInterval;
        }

        void Update()
        {
            if (autoSaveInterval <= 0f || Time.unscaledTime < nextAutoSaveTime) return;
            nextAutoSaveTime = Time.unscaledTime + autoSaveInterval;
            if (GameSave.IsDirty) GameSave.Save();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) GameSave.Save();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && saveOnFocusLost) GameSave.Save();
        }

        void OnApplicationQuit()
        {
            GameSave.Save();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
