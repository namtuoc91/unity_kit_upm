using UnityEngine;

namespace Raccoon.Save
{
    /// <summary>
    /// Settings for GameSave. Pass to GameSave.Configure() before the first GameSave call
    /// (ex: from a [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] method).
    /// </summary>
    public class SaveOptions
    {
        //File name inside Application.persistentDataPath. A .bak and a .tmp file are kept next to it
        public string FileName = "raccoon_save.dat";

        //AES + HMAC so players can't edit the file by hand (rooted / jailbroken devices).
        //It is obfuscation only: the key ships inside the app
        public bool Encrypt = true;

        //Null/empty = derived from Application.identifier. Never tie the key to the device,
        //saves restored from iCloud / Android Auto Backup on a new device must still decrypt
        public string EncryptKey;

        //The editor writes plain, indented JSON by default so the file is easy to read
        public bool EncryptInEditor = false;

        //Seconds between automatic saves while data is dirty, 0 = only save on pause / focus lost / quit / Save()
        public float AutoSaveInterval = 0f;

        //Android: saves when the app loses focus (notification shade, system dialogs...) as well as on pause
        public bool SaveOnFocusLost = true;

        //Version of the save format. Bump it and register a migration with GameSave.RegisterMigration when the data layout changes
        public int CurrentVersion = 1;

        //Custom storage (cloud...). Null = FileSaveStorage built from the settings above
        public ISaveStorage Storage;

        internal string ResolvedKey => string.IsNullOrEmpty(EncryptKey) ? DefaultKey : EncryptKey;

        internal static string DefaultKey
        {
            get
            {
                string id = string.IsNullOrEmpty(Application.identifier) ? Application.productName : Application.identifier;
                return id + "|raccoon.save";
            }
        }
    }
}
