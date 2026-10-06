using System;
using System.Collections.Generic;
using System.Threading.Tasks;
#if USING_FIREBASE
using Firebase.Analytics;
using Firebase.Crashlytics;
using Firebase.Extensions;
#endif
using UnityEngine;

namespace Raccoon.GameService
{
    public class GameFirebase : MonoBehaviour
    {
        #region Remote Config (shared API)

        /// Fired once on the main thread when remote config is usable (fetched, or fell back to cache/defaults).
        /// Requires a GameFirebase instance in the scene when USING_FIREBASE is on.
#pragma warning disable CS0067 // never raised when USING_FIREBASE is off
        public static event Action OnRemoteConfigReady;
#pragma warning restore CS0067

#if USING_FIREBASE
        public static bool IsRemoteConfigReady { get; private set; }
#else
        public static bool IsRemoteConfigReady => true;
#endif

        /// Invokes the callback immediately if remote config is ready, otherwise once it becomes ready.
        public static void WhenRemoteConfigReady(Action callback)
        {
            if (callback == null) return;
            if (IsRemoteConfigReady) callback();
            else OnRemoteConfigReady += callback;
        }

        #endregion

        // Static state survives Play Mode when Domain Reload is disabled, so reset it explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            OnRemoteConfigReady = null;
#if USING_FIREBASE
            api = null;
            firebaseInitialized = false;
            IsRemoteConfigReady = false;
            pendingRemoteDefaults = null;
#endif
        }

        // Analytics values like "10.0" are sent as "10"; other values are untouched.
        private static string NormalizeParamValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            return value.EndsWith(".0", StringComparison.Ordinal) ? value.Substring(0, value.Length - 2) : value;
        }

#if USING_FIREBASE
        private static GameFirebase api;
        public static GameFirebase Instance => api;

        private static bool firebaseInitialized = false;
        private Firebase.FirebaseApp appFirebase;

        // Start is called before the first frame update
        void Awake()
        {
            if(api != null)
            {
                Destroy(gameObject);
                return;
            }
            api = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            InitFirebase();
        }

        private void InitFirebase()
        {
            if (firebaseInitialized) return;
            //Debug.Log("start init");
            Firebase.FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError("Firebase dependency check failed: " + task.Exception);
                    NotifyRemoteConfigReady();
                    return;
                }

                var dependencyStatus = task.Result;
                if (dependencyStatus == Firebase.DependencyStatus.Available)
                {
                    InitializeFirebase();
                }
                else
                {
                    UnityEngine.Debug.LogError(System.String.Format(
                      "Could not resolve all Firebase dependencies: {0}", dependencyStatus));
                    // Firebase Unity SDK is not safe to use here.
                    // Unblock waiters so the game falls back to default values.
                    NotifyRemoteConfigReady();
                }
            });
        }

        // Handle initialization of the necessary firebase modules:
        void InitializeFirebase()
        {
            //DebugLog("Enabling data collection.");
            FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Firebase.Analytics.FirebaseAnalytics.LogEvent("test_event");
#endif
            // Crashlytics reports LogType.Exception logs on its own; this makes uncaught ones fatal.
            Crashlytics.ReportUncaughtExceptionsAsFatal = true;
            appFirebase = Firebase.FirebaseApp.DefaultInstance;

            firebaseInitialized = true;

            Debug.Log("Initialize Firebase.");

            LoadRemoteConfig();

            // SetConsentData();
        }

        private void SetConsentData()
        {
            Dictionary<ConsentType, ConsentStatus> consentMap = new Dictionary<ConsentType, ConsentStatus>();
            consentMap.Add(ConsentType.AnalyticsStorage, ConsentStatus.Granted);
            consentMap.Add(ConsentType.AdPersonalization, ConsentStatus.Granted);
            consentMap.Add(ConsentType.AdUserData, ConsentStatus.Granted);
            consentMap.Add(ConsentType.AdStorage, ConsentStatus.Granted);
            Firebase.Analytics.FirebaseAnalytics.SetConsent(consentMap);
        }

        // Release builds cache fetched values to avoid Firebase throttling; debug builds always fetch fresh.
        private static readonly TimeSpan ReleaseFetchCacheExpiration = TimeSpan.FromHours(1);

        private static Dictionary<string, object> pendingRemoteDefaults;

        /// In-app default values, used before the first successful fetch. Call before Firebase initializes (e.g. in Awake).
        public static void SetRemoteConfigDefaults(IDictionary<string, object> defaults)
        {
            if (defaults == null) return;
            pendingRemoteDefaults ??= new Dictionary<string, object>();
            foreach (var pair in defaults)
                pendingRemoteDefaults[pair.Key] = pair.Value;

            if (firebaseInitialized)
                Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.SetDefaultsAsync(pendingRemoteDefaults);
        }

        private void LoadRemoteConfig()
        {
            Debug.Log("Load remote config");

            var remoteConfig = Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance;
            var cacheExpiration = Debug.isDebugBuild ? TimeSpan.Zero : ReleaseFetchCacheExpiration;

            Task setDefaultsTask = pendingRemoteDefaults != null
                ? remoteConfig.SetDefaultsAsync(pendingRemoteDefaults)
                : Task.CompletedTask;

            setDefaultsTask
                .ContinueWithOnMainThread(_ => remoteConfig.FetchAsync(cacheExpiration))
                .Unwrap()
                .ContinueWithOnMainThread(FetchDataComplete);
        }

        private static void NotifyRemoteConfigReady()
        {
            if (IsRemoteConfigReady) return;
            IsRemoteConfigReady = true;

            var callbacks = OnRemoteConfigReady;
            OnRemoteConfigReady = null;
            if (callbacks == null) return;

            // Invoke each listener separately so one failing listener does not block the rest.
            foreach (Action callback in callbacks.GetInvocationList())
            {
                try { callback(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        void FetchDataComplete(Task fetchTask)
        {
            if (fetchTask.IsCanceled)
            {
                Debug.Log("Fetch canceled.");
            }
            else if (fetchTask.IsFaulted)
            {
                Debug.Log("Fetch encountered an error.");
            }
            else if (fetchTask.IsCompleted)
            {
                Debug.Log("Fetch completed successfully!");
            }

            var info = Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.Info;
            switch (info.LastFetchStatus)
            {
                case Firebase.RemoteConfig.LastFetchStatus.Success:
                    Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.ActivateAsync()
                    .ContinueWithOnMainThread(task => {
                        if (task.IsFaulted)
                            Debug.LogWarning("Remote config activate failed: " + task.Exception);
                        NotifyRemoteConfigReady();
                    });
                    // Ready is notified after activation completes.
                    return;
                case Firebase.RemoteConfig.LastFetchStatus.Failure:
                    switch (info.LastFetchFailureReason)
                    {
                        case Firebase.RemoteConfig.FetchFailureReason.Error:
                            Debug.Log("Fetch failed for unknown reason");
                            break;
                        case Firebase.RemoteConfig.FetchFailureReason.Throttled:
                            Debug.Log("Fetch throttled until " + info.ThrottledEndTime);
                            break;
                    }
                    break;
                case Firebase.RemoteConfig.LastFetchStatus.Pending:
                    Debug.Log("Latest Fetch call still pending.");
                    break;
            }

            // Fetch failed/pending: previously activated values or defaults are still usable.
            NotifyRemoteConfigReady();
        }

        private static bool TryGetRemoteValue(string key, out Firebase.RemoteConfig.ConfigValue value)
        {
            value = default;
            if (!firebaseInitialized || string.IsNullOrEmpty(key)) return false;

            value = Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue(key);
            // Static source means the key exists neither remotely nor in in-app defaults.
            return value.Source != Firebase.RemoteConfig.ValueSource.StaticValue;
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            try { return TryGetRemoteValue(key, out var v) ? v.BooleanValue : defaultValue; }
            catch (Exception) { return defaultValue; }
        }

        public static long GetLong(string key, long defaultValue = 0)
        {
            try { return TryGetRemoteValue(key, out var v) ? v.LongValue : defaultValue; }
            catch (Exception) { return defaultValue; }
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            return (int)GetLong(key, defaultValue);
        }

        public static double GetDouble(string key, double defaultValue = 0)
        {
            try { return TryGetRemoteValue(key, out var v) ? v.DoubleValue : defaultValue; }
            catch (Exception) { return defaultValue; }
        }

        public static float GetFloat(string key, float defaultValue = 0f)
        {
            return (float)GetDouble(key, defaultValue);
        }

        public static string GetString(string key, string defaultValue = "")
        {
            return TryGetRemoteValue(key, out var v) ? v.StringValue : defaultValue;
        }

        /// Parses a JSON string value into T via JsonUtility. Returns defaultValue if missing or invalid.
        public static T GetJson<T>(string key, T defaultValue = default)
        {
            var json = GetString(key, null);
            if (string.IsNullOrEmpty(json)) return defaultValue;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception ex)
            {
                Debug.LogWarning($"Remote config '{key}' is not valid JSON for {typeof(T).Name}: {ex.Message}");
                return defaultValue;
            }
        }

        private static void SendEventFirebase(string nameEvent, params string[] parameters)
        {
#if UNITY_EDITOR
            var txt = $"send firebase :{nameEvent}: ";
            for (int i = 0; i + 1 < parameters.Length; i += 2)
            {
                txt += $"{parameters[i]}: {NormalizeParamValue(parameters[i + 1])},";
            }
            //Debug.Log(txt);
#endif
            if (!firebaseInitialized) return;

            var arr = new List<Firebase.Analytics.Parameter>();
            if (parameters.Length % 2 != 0) return;
            for (int i = 0; i < parameters.Length; i += 2)
            {
                arr.Add(new Firebase.Analytics.Parameter(parameters[i], NormalizeParamValue(parameters[i + 1])));
            }
            Firebase.Analytics.FirebaseAnalytics.LogEvent(nameEvent, arr.ToArray());
        }

        public static void SendEvent(string nameEvent, params string[] parameters)
        {
            try
            {
#if UNITY_EDITOR
                SendEventFirebase(nameEvent, parameters);
#endif

                if (Instance == null || Instance.appFirebase == null)
                {
                    Debug.LogWarning("Firebase not initialized yet. Cannot use feature.");
                    return;
                }
                if (firebaseInitialized)
                    SendEventFirebase(nameEvent, parameters);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"SendEvent '{nameEvent}' failed: {ex}");
            }
        }

        public static void SendEventGame(string content)
        {
            SendEvent(Event_Firebase.EVENT_GAME, Event_Firebase.EVENT_GAME, content);
        }

        // Log a caught exception.
        public void LogCaughtException(Exception ex)
        {
            Crashlytics.LogException(ex);
        }

        void OnDestroy()
        {
            if (api == this) api = null;
        }
#else
        // Firebase disabled: remote config getters always return the provided default.
        public static void SetRemoteConfigDefaults(IDictionary<string, object> defaults)
        {
        }

        public static bool GetBool(string key, bool defaultValue = false) => defaultValue;
        public static long GetLong(string key, long defaultValue = 0) => defaultValue;
        public static int GetInt(string key, int defaultValue = 0) => defaultValue;
        public static double GetDouble(string key, double defaultValue = 0) => defaultValue;
        public static float GetFloat(string key, float defaultValue = 0f) => defaultValue;
        public static string GetString(string key, string defaultValue = "") => defaultValue;
        public static T GetJson<T>(string key, T defaultValue = default) => defaultValue;

        private static void SendEventFirebase(string nameEvent, params string[] parameters)
        {
            var txt = $"send firebase :{nameEvent}: ";
            for (int i = 0; i + 1 < parameters.Length; i += 2)
            {
                txt += $"{parameters[i]}: {NormalizeParamValue(parameters[i + 1])},";
            }
            //Debug.Log(txt);
        }

        public static void SendEvent(string nameEvent, params string[] parameters)
        {
#if UNITY_EDITOR
            SendEventFirebase(nameEvent, parameters);
#endif
        }

        public static void SendEventGame(string content)
        {
            SendEvent(Event_Firebase.EVENT_GAME, Event_Firebase.EVENT_GAME, content);
        }
#endif
    }

    public class Event_Firebase
    {
        public readonly static string EVENT_GAME = "event_game";
        public readonly static string EVENT_PURCHASE_IAP = "purchase_iap";
    }
}
