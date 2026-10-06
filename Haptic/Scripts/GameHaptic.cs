using System.Threading;
using UnityEngine;
#if USING_HAPTIC && UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Scripting;
#endif
#if USING_HAPTIC && UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Raccoon.Haptic
{
    public enum HapticType
    {
        Selection,  //Very light tick, for scrolling pickers, toggles
        Light,      //Light impact, for button clicks
        Medium,     //Medium impact, for collisions, collecting items
        Heavy,      //Strong impact, for explosions, big hits
        Success,    //Notification pattern, for level complete, purchase success
        Warning,    //Notification pattern, for invalid actions
        Failure,    //Notification pattern, for game over, errors
    }

    /// <summary>
    /// Manager script to play haptic feedback (vibration) on mobile.
    /// Android uses Vibrator/VibrationEffect through JNI, iOS uses UIFeedbackGenerator through the native plugin Plugins/iOS/GameHaptic.mm.
    /// Does nothing on other platforms.
    /// Add USING_HAPTIC to Scripting Define Symbols to enable it. Without it the API still compiles but does nothing,
    /// and the VIBRATE permission is not added to the Android manifest.
    /// Must be called from the main thread, use ContinueWithOnMainThread for SDK callbacks (Firebase, Ads...)
    /// </summary>

    public class GameHaptic : MonoBehaviour
    {
        private static GameHaptic instance;
        public static GameHaptic Instance => instance;

        private const string KEY_HAPTIC_ENABLED = "GameHaptic_Enabled";

        [Tooltip("Minimum time in seconds between two light haptics (Selection, Light, Medium, Vibrate), avoids spamming the motor (ex: many collisions in one frame). Heavy and notifications always play")]
        [SerializeField] private float minInterval = 0.05f;
        [Tooltip("Logs haptic calls in the editor, since the editor can't vibrate")]
        [SerializeField] private bool debugLog;

        private bool isHapticEnabled = true;
        private bool settingsDirty;
        private float lastHapticTime = float.MinValue;
        private int mainThreadId;
#if UNITY_EDITOR
        private static bool missingInstanceWarned;
#endif

        public bool IsHapticEnabled => isHapticEnabled;
        public bool IsSupported { get; private set; }

#if UNITY_EDITOR
        //Statics are not reset between plays when Domain Reload is disabled (Enter Play Mode Options)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            missingInstanceWarned = false;
        }
#endif

        void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject); return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            LoadSettings();
            IsSupported = InitPlatform();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                SaveSettings();
                Stop();
                ReleasePlatform();
                instance = null;
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                SaveSettings();
                Stop();
            }
        }

        private void OnApplicationQuit()
        {
            SaveSettings();
        }

        private void LoadSettings()
        {
            isHapticEnabled = PlayerPrefs.GetInt(KEY_HAPTIC_ENABLED, 1) == 1;
        }

        //Writing to disk is slow on mobile, so settings are only flushed on pause/quit/destroy instead of on every change
        public void SaveSettings()
        {
            if (!settingsDirty)
                return;
            PlayerPrefs.SetInt(KEY_HAPTIC_ENABLED, isHapticEnabled ? 1 : 0);
            PlayerPrefs.Save();
            settingsDirty = false;
        }

        public void SetEnableHaptic(bool enable)
        {
            if (isHapticEnabled == enable)
                return;

            isHapticEnabled = enable;
            settingsDirty = true;
            if (!enable)
                Stop();
        }

        //ignoreInterval: true to play even if another haptic was played less than minInterval ago
        //Heavy and notifications (Success, Warning, Failure) are never blocked by minInterval, they are important feedback
        public void PlayHaptic(HapticType type, bool ignoreInterval = false)
        {
            if (!CanPlay(ignoreInterval || !IsLightHaptic(type)))
                return;

            Log(type.ToString());
            PlatformPlay(type);
        }

        //Custom vibration. duration in seconds, amplitude 0..1 (0 = no vibration, on Android only used by devices with amplitude control)
        //On iOS there is no custom duration, it is mapped to the closest impact
        public void PlayVibrate(float duration, float amplitude = 1f, bool ignoreInterval = false)
        {
            amplitude = Mathf.Clamp01(amplitude);
            if (duration <= 0f || amplitude <= 0f || !CanPlay(ignoreInterval))
                return;

            Log($"Vibrate {duration}s amplitude {amplitude}");
            PlatformVibrate(duration, amplitude);
        }

        //Stops the current vibration (Android only, iOS haptics are too short to be stopped)
        public void Stop()
        {
#if USING_HAPTIC && UNITY_ANDROID && !UNITY_EDITOR
            if (vibrator != null)
                vibrator.Call("cancel");
#endif
        }

        private static bool IsLightHaptic(HapticType type)
        {
            return type == HapticType.Selection || type == HapticType.Light || type == HapticType.Medium;
        }

        private bool CanPlay(bool ignoreInterval)
        {
            if (!isHapticEnabled || !IsSupported)
                return false;

            //Time and JNI calls are not allowed outside the main thread
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
            {
                Debug.LogWarning("[GameHaptic] Haptic called from a background thread, ignored. Call it from the main thread");
                return false;
            }

            float now = Time.unscaledTime;
            if (!ignoreInterval && now - lastHapticTime < minInterval)
                return false;

            lastHapticTime = now;
            return true;
        }

        private void Log(string msg)
        {
#if UNITY_EDITOR
            if (debugLog)
                Debug.Log("[GameHaptic] " + msg);
#endif
        }

        //ReferenceEquals instead of UnityEngine.Object == which can call native code and throw outside the main thread
        //Safe because OnDestroy sets instance to null
        private static bool HasInstance()
        {
            bool hasInstance = !ReferenceEquals(instance, null);
#if UNITY_EDITOR
            if (!hasInstance && !missingInstanceWarned)
            {
                missingInstanceWarned = true;
                Debug.LogWarning("[GameHaptic] No GameHaptic in the scene, haptics are ignored. Add the GameHaptic component to a GameObject");
            }
#endif
            return hasInstance;
        }

        //Shortcuts
        public static void Play(HapticType type) { if (HasInstance()) instance.PlayHaptic(type); }
        public static void Vibrate(float duration, float amplitude = 1f) { if (HasInstance()) instance.PlayVibrate(duration, amplitude); }
        public static void Selection() => Play(HapticType.Selection);
        public static void Light() => Play(HapticType.Light);
        public static void Medium() => Play(HapticType.Medium);
        public static void Heavy() => Play(HapticType.Heavy);
        public static void Success() => Play(HapticType.Success);
        public static void Warning() => Play(HapticType.Warning);
        public static void Failure() => Play(HapticType.Failure);

        //---------- Platform ----------

#if !USING_HAPTIC
        private bool InitPlatform() => false; //Haptic disabled, add USING_HAPTIC to Scripting Define Symbols to enable it
        private void ReleasePlatform() { }
        private void PlatformPlay(HapticType type) { }
        private void PlatformVibrate(float duration, float amplitude) { }

#elif UNITY_EDITOR
        private bool InitPlatform() => true; //Allows testing the logic (enable, interval, logs) in the editor
        private void ReleasePlatform() { }
        private void PlatformPlay(HapticType type) { }
        private void PlatformVibrate(float duration, float amplitude) { }

#elif UNITY_ANDROID
        //Values from android.os.VibrationEffect
        private const int DEFAULT_AMPLITUDE = -1;
        private const int EFFECT_TICK = 2;

        private AndroidJavaObject vibrator;
        private AndroidJavaClass vibrationEffectClass;
        private int sdkInt;
        private bool hasAmplitudeControl;

        private bool InitPlatform()
        {
            try
            {
                using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
                    sdkInt = version.GetStatic<int>("SDK_INT");

                using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                if (vibrator == null || !vibrator.Call<bool>("hasVibrator"))
                {
                    ReleasePlatform();
                    return false;
                }

                if (sdkInt >= 26)
                {
                    vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    hasAmplitudeControl = vibrator.Call<bool>("hasAmplitudeControl");
                }
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GameHaptic] Android init failed: " + e.Message);
                ReleasePlatform();
                return false;
            }
        }

        private void ReleasePlatform()
        {
            vibrator?.Dispose();
            vibrator = null;
            vibrationEffectClass?.Dispose();
            vibrationEffectClass = null;
        }

        private void PlatformPlay(HapticType type)
        {
            switch (type)
            {
                case HapticType.Selection:
                    if (sdkInt >= 29)
                        AndroidPredefined(EFFECT_TICK);
                    else
                        AndroidOneShot(15, 60);
                    break;
                case HapticType.Light: AndroidOneShot(20, 80); break;
                case HapticType.Medium: AndroidOneShot(40, 160); break;
                case HapticType.Heavy: AndroidOneShot(70, 255); break;
                //Waveforms: timings alternate off/on starting with a delay, amplitudes match timings
                case HapticType.Success: AndroidWaveform(new long[] { 0, 35, 65, 25 }, new int[] { 0, 200, 0, 255 }); break;
                case HapticType.Warning: AndroidWaveform(new long[] { 0, 30, 80, 30 }, new int[] { 0, 255, 0, 160 }); break;
                case HapticType.Failure: AndroidWaveform(new long[] { 0, 30, 50, 30, 50, 60 }, new int[] { 0, 200, 0, 200, 0, 255 }); break;
            }
        }

        private void PlatformVibrate(float duration, float amplitude)
        {
            //createOneShot throws on 0ms, amplitude must be 1..255
            AndroidOneShot(System.Math.Max(1L, (long)(duration * 1000f)), Mathf.Clamp(Mathf.RoundToInt(amplitude * 255f), 1, 255));
        }

        private void AndroidOneShot(long milliseconds, int amplitude)
        {
            try
            {
                if (sdkInt >= 26)
                {
                    using (AndroidJavaObject effect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, hasAmplitudeControl ? amplitude : DEFAULT_AMPLITUDE))
                        vibrator.Call("vibrate", effect);
                }
                else
                {
                    vibrator.Call("vibrate", milliseconds);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GameHaptic] Vibrate failed: " + e.Message);
            }
        }

        private void AndroidPredefined(int effectId)
        {
            try
            {
                using (AndroidJavaObject effect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createPredefined", effectId))
                    vibrator.Call("vibrate", effect);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GameHaptic] Vibrate failed: " + e.Message);
            }
        }

        private void AndroidWaveform(long[] timings, int[] amplitudes)
        {
            try
            {
                if (sdkInt >= 26)
                {
                    AndroidJavaObject effect = hasAmplitudeControl
                        ? vibrationEffectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1)
                        : vibrationEffectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, -1);
                    using (effect)
                        vibrator.Call("vibrate", effect);
                }
                else
                {
                    vibrator.Call("vibrate", timings, -1); //-1: don't repeat
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GameHaptic] Vibrate failed: " + e.Message);
            }
        }

        //Never called, referencing Handheld.Vibrate makes Unity add the VIBRATE permission to the Android manifest
        //[Preserve] keeps it from being stripped. Check the built manifest contains android.permission.VIBRATE
        [Preserve]
        private static void RequireVibratePermission() => Handheld.Vibrate();

#elif UNITY_IOS
        [DllImport("__Internal")] private static extern void _GameHaptic_Prepare();
        [DllImport("__Internal")] private static extern void _GameHaptic_Selection();
        [DllImport("__Internal")] private static extern void _GameHaptic_Impact(int style, float intensity);
        [DllImport("__Internal")] private static extern void _GameHaptic_Notification(int type);

        //Values from UIImpactFeedbackStyle / UINotificationFeedbackType
        private const int IMPACT_LIGHT = 0;
        private const int IMPACT_MEDIUM = 1;
        private const int IMPACT_HEAVY = 2;
        private const int NOTIFICATION_SUCCESS = 0;
        private const int NOTIFICATION_WARNING = 1;
        private const int NOTIFICATION_ERROR = 2;

        private bool InitPlatform()
        {
            _GameHaptic_Prepare();
            return true;
        }

        private void ReleasePlatform() { }

        private void PlatformPlay(HapticType type)
        {
            switch (type)
            {
                case HapticType.Selection: _GameHaptic_Selection(); break;
                case HapticType.Light: _GameHaptic_Impact(IMPACT_LIGHT, 1f); break;
                case HapticType.Medium: _GameHaptic_Impact(IMPACT_MEDIUM, 1f); break;
                case HapticType.Heavy: _GameHaptic_Impact(IMPACT_HEAVY, 1f); break;
                case HapticType.Success: _GameHaptic_Notification(NOTIFICATION_SUCCESS); break;
                case HapticType.Warning: _GameHaptic_Notification(NOTIFICATION_WARNING); break;
                case HapticType.Failure: _GameHaptic_Notification(NOTIFICATION_ERROR); break;
            }
        }

        private void PlatformVibrate(float duration, float amplitude)
        {
            int style = duration < 0.03f ? IMPACT_LIGHT : duration < 0.06f ? IMPACT_MEDIUM : IMPACT_HEAVY;
            _GameHaptic_Impact(style, amplitude);
        }

#else
        private bool InitPlatform() => false;
        private void ReleasePlatform() { }
        private void PlatformPlay(HapticType type) { }
        private void PlatformVibrate(float duration, float amplitude) { }
#endif
    }

}
