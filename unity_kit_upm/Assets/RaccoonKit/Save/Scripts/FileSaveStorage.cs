using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Raccoon.Save
{
    /// <summary>
    /// Local file storage with atomic writes: data goes to "file.tmp" first, the current file becomes "file.bak",
    /// then the .tmp is renamed. If the app is killed mid-write, the next read falls back to .tmp (if complete) or .bak.
    /// A file that can't be read is copied to "file.corrupt" before being overwritten, for debugging.
    /// </summary>
    public class FileSaveStorage : ISaveStorage
    {
        private readonly bool encrypt;
        private readonly string key;
        private readonly bool allowPlain;

        public string FilePath { get; }
        public string TempPath => FilePath + ".tmp";
        public string BackupPath => FilePath + ".bak";
        public string CorruptPath => FilePath + ".corrupt";

        /// <param name="filePath">Absolute path of the save file</param>
        /// <param name="encrypt">Write encrypted data</param>
        /// <param name="key">Key used to decrypt (always, if the file is encrypted) and to encrypt (when encrypt is true)</param>
        /// <param name="allowPlain">Accept a plain JSON file on read. Turned off in builds when encrypting, so a hand-written plain file is rejected</param>
        public FileSaveStorage(string filePath, bool encrypt, string key, bool allowPlain)
        {
            FilePath = filePath;
            this.encrypt = encrypt;
            this.key = key;
            this.allowPlain = allowPlain;
        }

        public bool TryRead(out string json)
        {
            //.tmp before .bak: if it is complete it is newer (the app died between the two renames)
            string[] candidates = { FilePath, TempPath, BackupPath };
            bool mainCorrupt = false;
            foreach (string path in candidates)
            {
                if (!File.Exists(path)) continue;
                if (TryReadFile(path, out json))
                {
                    if (path != FilePath)
                    {
                        Debug.LogWarning($"[GameSave] {Path.GetFileName(FilePath)} is missing or corrupt, restored from {Path.GetFileName(path)}");
                        if (mainCorrupt) KeepCorruptCopy();
                    }
                    return true;
                }
                Debug.LogError($"[GameSave] Can't read {path} (corrupt, tampered, or encrypted with another key)");
                if (path == FilePath) mainCorrupt = true;
            }
            if (mainCorrupt) KeepCorruptCopy();
            json = null;
            return false;
        }

        public void Write(string json)
        {
            byte[] bytes = encrypt ? SaveCrypto.Encrypt(json, key) : Encoding.UTF8.GetBytes(json);

            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (FileStream fs = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            //Manual rotate instead of File.Replace, which is not reliable on every mobile runtime
            if (File.Exists(FilePath))
            {
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                File.Move(FilePath, BackupPath);
            }
            File.Move(TempPath, FilePath);
        }

        public void Delete()
        {
            foreach (string path in new[] { FilePath, TempPath, BackupPath, CorruptPath })
                if (File.Exists(path)) File.Delete(path);
        }

        internal bool TryReadFile(string path, out string json)
        {
            json = null;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSave] {e.Message}");
                return false;
            }

            if (SaveCrypto.HasMagic(bytes))
            {
                if (string.IsNullOrEmpty(key) || !SaveCrypto.TryDecrypt(bytes, key, out json)) return false;
            }
            else
            {
                if (!allowPlain) return false;
                json = Encoding.UTF8.GetString(bytes);
            }
            return SaveData.TryParseEnvelope(json, out _, out _);
        }

        private void KeepCorruptCopy()
        {
            try
            {
                if (File.Exists(FilePath)) File.Copy(FilePath, CorruptPath, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSave] Can't keep a copy of the corrupt save: {e.Message}");
            }
        }
    }
}
