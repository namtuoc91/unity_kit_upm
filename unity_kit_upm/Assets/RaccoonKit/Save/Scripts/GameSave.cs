using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Raccoon.Save
{
    public enum PlayerPrefsType
    {
        Int,
        Float,
        String,
    }

    /// <summary>
    /// Local save for mobile games: key / value data (primitives, classes, List, Dictionary, Unity structs) stored as JSON
    /// in Application.persistentDataPath, encrypted by default, written atomically with a backup copy.
    /// Loads lazily on the first call, no scene object needed. Saves on app pause / focus lost / quit,
    /// every AutoSaveInterval seconds, or when Save() is called. Set only changes memory until then.
    /// Needs Newtonsoft JSON (com.unity.nuget.newtonsoft-json), which turns on USING_GAMESAVE. Without it the API
    /// still compiles but does nothing: Get returns the default value and nothing is written.
    /// Must be called from the main thread, use ContinueWithOnMainThread for SDK callbacks (Firebase, Ads...)
    /// </summary>
    public static class GameSave
    {
        private static SaveOptions options;
        private static ISaveStorage storage;
        private static SaveData data;
        private static int dataVersion;
        private static bool isLoaded;
        private static bool isDirty;
        private static readonly Dictionary<int, Action<SaveData>> migrations = new Dictionary<int, Action<SaveData>>();
        private static int mainThreadId;

#pragma warning disable CS0414 //Never raised without USING_GAMESAVE
        public static event Action OnLoaded;
#pragma warning restore CS0414
        //Called right before data is written, a last chance to Set values (play time, timestamps...)
        public static event Action OnBeforeSave;

#if USING_GAMESAVE
        public static bool IsEnabled => true;
#else
        public static bool IsEnabled => false;
#endif
        public static bool IsLoaded => isLoaded;
        public static bool IsDirty => isDirty;
        public static SaveOptions Options => options ?? (options = new SaveOptions());

        public static ICollection<string> Keys
        {
            get { Ready(); return data.Keys; }
        }

        internal static SaveData Data
        {
            get { Ready(); return data; }
        }

        //Statics are not reset between plays when Domain Reload is disabled (Enter Play Mode Options)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            options = null;
            storage = null;
            data = null;
            isLoaded = false;
            isDirty = false;
            migrations.Clear();
            OnLoaded = null;
            OnBeforeSave = null;
        }

        #region Setup
        /// <summary>Call before the first GameSave call, ex: in a [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] method</summary>
        public static void Configure(SaveOptions newOptions)
        {
            if (newOptions == null) throw new ArgumentNullException(nameof(newOptions));
            if (isLoaded)
            {
                Debug.LogError("[GameSave] Configure must be called before the first GameSave call, ignored");
                return;
            }
            if (newOptions.CurrentVersion < 1) newOptions.CurrentVersion = 1;
            options = newOptions;
        }

        /// <summary>
        /// Upgrades data saved with version fromVersion to fromVersion + 1. Migrations run in order on load
        /// until CurrentVersion is reached. Register them before the first GameSave call.
        /// </summary>
        public static void RegisterMigration(int fromVersion, Action<SaveData> migrate)
        {
            if (migrate == null) throw new ArgumentNullException(nameof(migrate));
            if (isLoaded)
            {
                Debug.LogError($"[GameSave] RegisterMigration({fromVersion}) must be called before the first GameSave call, ignored");
                return;
            }
            migrations[fromVersion] = migrate;
        }
        #endregion

        #region Data
        public static T Get<T>(string key, T defaultValue = default)
        {
            Ready();
            return data.Get(key, defaultValue);
        }

        public static bool TryGet<T>(string key, out T value)
        {
            Ready();
            return data.TryGet(key, out value);
        }

        //Changes memory only, the file is written on pause / quit / auto-save / Save()
        public static void Set<T>(string key, T value)
        {
            Ready();
            if (data.Set(key, value)) isDirty = true;
        }

        public static bool HasKey(string key)
        {
            Ready();
            return data.HasKey(key);
        }

        public static void Delete(string key)
        {
            Ready();
            if (data.Delete(key)) isDirty = true;
        }

        //Clears every key, call Save() to write the empty save right away
        public static void DeleteAll()
        {
            Ready();
            if (data.Count == 0) return;
            data.Clear();
            isDirty = true;
        }

        /// <summary>
        /// Writes the data now if it changed. Call it after important changes (IAP, rewards...).
        /// Returns false if writing failed (disk full...): the data stays in memory and is retried on the next save.
        /// </summary>
        public static bool Save()
        {
            if (!isLoaded || !isDirty) return true;
            CheckMainThread();
            Invoke(OnBeforeSave, nameof(OnBeforeSave));
            try
            {
                storage.Write(data.ToEnvelopeJson(dataVersion, Application.isEditor));
                isDirty = false;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameSave] Save failed, data kept in memory: {e}");
                return false;
            }
        }

        /// <summary>
        /// Copies a PlayerPrefs value into the save (for games already live with PlayerPrefs).
        /// Skipped if the PlayerPrefs key doesn't exist or the save already has saveKey.
        /// The PlayerPrefs key is only deleted once the save file is written.
        /// </summary>
        public static bool ImportFromPlayerPrefs(string prefsKey, PlayerPrefsType type, string saveKey = null, bool deletePrefsKey = true)
        {
            Ready();
            if (saveKey == null) saveKey = prefsKey;
            if (!PlayerPrefs.HasKey(prefsKey) || data.HasKey(saveKey)) return false;

            bool changed;
            switch (type)
            {
                case PlayerPrefsType.Int: changed = data.Set(saveKey, PlayerPrefs.GetInt(prefsKey)); break;
                case PlayerPrefsType.Float: changed = data.Set(saveKey, PlayerPrefs.GetFloat(prefsKey)); break;
                default: changed = data.Set(saveKey, PlayerPrefs.GetString(prefsKey)); break;
            }
            if (!changed) return false;
            isDirty = true;
            if (!Save()) return false;

            if (deletePrefsKey)
            {
                PlayerPrefs.DeleteKey(prefsKey);
                PlayerPrefs.Save();
            }
            return true;
        }
        #endregion

        #region Load
        internal static void MarkDirty() => isDirty = true;

        private static void Ready()
        {
            CheckMainThread();
            if (!isLoaded) Load();
        }

        private static void Load()
        {
            SaveOptions opt = Options;
            isLoaded = true;
#if USING_GAMESAVE
            storage = opt.Storage ?? CreateFileStorage(opt);
            data = null;
            dataVersion = opt.CurrentVersion;
            try
            {
                if (storage.TryRead(out string json) && !SaveData.TryParseEnvelope(json, out data, out dataVersion))
                    Debug.LogError("[GameSave] Save data is not valid, starting with an empty save");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (data == null)
            {
                data = new SaveData();
                dataVersion = opt.CurrentVersion;
            }
            RunMigrations(opt.CurrentVersion);

            if (Application.isPlaying) SaveLifecycle.Create(opt);
            Invoke(OnLoaded, nameof(OnLoaded));
#else
            data = new SaveData();
            Debug.LogWarning("[GameSave] USING_GAMESAVE is not defined (install com.unity.nuget.newtonsoft-json), nothing is saved");
#endif
        }

        private static FileSaveStorage CreateFileStorage(SaveOptions opt)
        {
            bool encrypt = opt.Encrypt && (!Application.isEditor || opt.EncryptInEditor);
            string path = Path.Combine(Application.persistentDataPath, opt.FileName);
            //The key is always passed so an encrypted file still loads after turning encryption off
            return new FileSaveStorage(path, encrypt, opt.ResolvedKey, !encrypt || Application.isEditor);
        }

        private static void RunMigrations(int currentVersion)
        {
            if (dataVersion > currentVersion)
            {
                Debug.LogWarning($"[GameSave] Save version {dataVersion} is newer than CurrentVersion {currentVersion}, data is kept as is");
                return;
            }
            if (dataVersion == currentVersion) return;

            for (int v = dataVersion; v < currentVersion; v++)
            {
                if (!migrations.TryGetValue(v, out Action<SaveData> migrate)) continue;
                try
                {
                    migrate(data);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GameSave] Migration from version {v} failed: {e}");
                }
            }
            dataVersion = currentVersion;
            isDirty = true;
        }
        #endregion

        private static void Invoke(Action action, string name)
        {
            if (action == null) return;
            foreach (Delegate d in action.GetInvocationList())
            {
                try
                {
                    ((Action)d)();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GameSave] {name} handler threw: {e}");
                }
            }
        }

        private static void CheckMainThread()
        {
            if (mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != mainThreadId)
                Debug.LogError("[GameSave] Must be called from the main thread (use ContinueWithOnMainThread for SDK callbacks)");
        }
    }
}
