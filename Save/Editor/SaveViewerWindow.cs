// Raccoon → Save → Save Viewer: view / edit / delete GameSave data.
// Play mode: edits the live GameSave data (written on the next save). Edit mode: edits save files in
// Application.persistentDataPath directly (plain files, or files encrypted with the default key).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Raccoon.Save
{
    public class SaveViewerWindow : EditorWindow
    {
        [MenuItem("Raccoon/Save/Save Viewer...", priority = 100)]
        private static void Open()
        {
            GetWindow<SaveViewerWindow>("Save Viewer").minSize = new Vector2(420, 300);
        }

#if USING_GAMESAVE
        private const long MaxFileSize = 5 * 1024 * 1024;

        private string[] files = Array.Empty<string>();
        private int fileIndex;
        private FileSaveStorage fileStorage;
        private SaveData fileData;
        private int fileVersion;
        private string loadError;

        private string search = "";
        private Vector2 scroll;
        private readonly HashSet<string> expanded = new HashSet<string>();
        private readonly Dictionary<string, string> edits = new Dictionary<string, string>(); // key -> edited JSON, not applied yet
        private string newKey = "";
        private string newJson = "";

        private static bool Live => Application.isPlaying && GameSave.IsLoaded;
        private SaveData Current => Live ? GameSave.Data : fileData;

        [MenuItem("Raccoon/Save/Clear Save", priority = 101)]
        private static void ClearSave()
        {
            if (Application.isPlaying)
            {
                if (!EditorUtility.DisplayDialog("Clear Save", "Xoá toàn bộ data GameSave đang chạy và ghi file rỗng?", "Xoá", "Huỷ")) return;
                GameSave.DeleteAll();
                GameSave.Save();
                return;
            }

            string[] saves = FindSaveFiles();
            if (saves.Length == 0)
            {
                EditorUtility.DisplayDialog("Clear Save", $"Không có file save nào trong\n{Application.persistentDataPath}", "OK");
                return;
            }
            string names = string.Join("\n", saves.Select(Path.GetFileName));
            if (!EditorUtility.DisplayDialog("Clear Save", $"Xoá các file save (kèm .bak / .tmp)?\n\n{names}", "Xoá", "Huỷ")) return;
            foreach (string path in saves) CreateStorage(path).Delete();
            Debug.Log($"[GameSave] Deleted {saves.Length} save file(s)");
        }

        void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            RefreshFiles();
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        void OnInspectorUpdate()
        {
            if (Application.isPlaying) Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            edits.Clear();
            RefreshFiles();
            Repaint();
        }

        void OnGUI()
        {
            DrawToolbar();
            SaveData data = Current;
            if (data == null)
            {
                if (!string.IsNullOrEmpty(loadError)) EditorGUILayout.HelpBox(loadError, MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(4);
            search = EditorGUILayout.TextField("Search", search);
            EditorGUILayout.LabelField($"{data.Count} key", EditorStyles.miniLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string key in data.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
            {
                if (!string.IsNullOrEmpty(search) && key.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                DrawKey(data, key);
            }
            EditorGUILayout.EndScrollView();

            DrawAddKey(data);
        }

        private void DrawToolbar()
        {
            if (Application.isPlaying)
            {
                if (!GameSave.IsLoaded)
                {
                    EditorGUILayout.HelpBox("GameSave chưa load (game chưa gọi GameSave).", MessageType.Info);
                    if (GUILayout.Button("Load now")) _ = GameSave.Keys;
                    return;
                }
                EditorGUILayout.HelpBox("Play mode: đang sửa data live, ghi file ở lần save tiếp theo. Object game đã Get trước đó không tự cập nhật.", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(GameSave.IsDirty ? "● Có thay đổi chưa ghi" : "Đã ghi", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Save Now", GUILayout.Width(90))) GameSave.Save();
                if (GUILayout.Button("Delete All", GUILayout.Width(90)) &&
                    EditorUtility.DisplayDialog("Delete All", "Xoá toàn bộ key?", "Xoá", "Huỷ"))
                    GameSave.DeleteAll();
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (files.Length == 0)
            {
                GUILayout.Label("Không có file save trong persistentDataPath", EditorStyles.miniLabel);
            }
            else
            {
                int newIndex = EditorGUILayout.Popup("File", fileIndex, files.Select(Path.GetFileName).ToArray());
                if (newIndex != fileIndex)
                {
                    fileIndex = newIndex;
                    LoadFile();
                }
            }
            if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshFiles();
            if (GUILayout.Button("Open Folder", GUILayout.Width(90)))
                EditorUtility.RevealInFinder(files.Length > 0 ? files[fileIndex] : Application.persistentDataPath);
            using (new EditorGUI.DisabledScope(fileStorage == null))
            {
                if (GUILayout.Button("Delete File", GUILayout.Width(80)) &&
                    EditorUtility.DisplayDialog("Delete File", $"Xoá {Path.GetFileName(fileStorage.FilePath)} (kèm .bak / .tmp)?", "Xoá", "Huỷ"))
                {
                    fileStorage.Delete();
                    RefreshFiles();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawKey(SaveData data, string key)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            bool open = expanded.Contains(key);
            string header = open ? $"{key}  ({data.GetTokenType(key)})" : $"{key} = {Truncate(data.GetJson(key, false), 60)}";
            bool newOpen = EditorGUILayout.Foldout(open, header, true);
            if (newOpen != open)
            {
                if (newOpen) expanded.Add(key);
                else expanded.Remove(key);
            }
            if (GUILayout.Button("X", GUILayout.Width(22)) &&
                EditorUtility.DisplayDialog("Delete key", $"Xoá key '{key}'?", "Xoá", "Huỷ"))
            {
                data.Delete(key);
                edits.Remove(key);
                Commit(data, true);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (newOpen)
            {
                if (!edits.TryGetValue(key, out string text)) text = data.GetJson(key, true);
                string newText = EditorGUILayout.TextArea(text, GUILayout.MinHeight(36));
                if (newText != text || edits.ContainsKey(key)) edits[key] = newText;

                using (new EditorGUI.DisabledScope(!edits.ContainsKey(key)))
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Revert", GUILayout.Width(70)))
                    {
                        edits.Remove(key);
                        GUI.FocusControl(null);
                    }
                    if (GUILayout.Button("Apply", GUILayout.Width(70)) && edits.TryGetValue(key, out string json))
                    {
                        if (data.TrySetJson(key, json, out bool changed, out string error))
                        {
                            edits.Remove(key);
                            GUI.FocusControl(null);
                            Commit(data, changed);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("JSON không hợp lệ", error, "OK");
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAddKey(SaveData data)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Thêm key", EditorStyles.boldLabel);
            newKey = EditorGUILayout.TextField("Key", newKey);
            EditorGUILayout.LabelField("Value (JSON: 100, \"text\", true, {\"level\":1})", EditorStyles.miniLabel);
            newJson = EditorGUILayout.TextArea(newJson, GUILayout.MinHeight(36));
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(newKey) || string.IsNullOrEmpty(newJson)))
            {
                if (!GUILayout.Button("Add")) return;
            }
            if (data.HasKey(newKey))
            {
                EditorUtility.DisplayDialog("Thêm key", $"Key '{newKey}' đã tồn tại.", "OK");
                return;
            }
            if (!data.TrySetJson(newKey, newJson, out _, out string error))
            {
                EditorUtility.DisplayDialog("JSON không hợp lệ", error, "OK");
                return;
            }
            newKey = "";
            newJson = "";
            GUI.FocusControl(null);
            Commit(data, true);
        }

        //Live: marks GameSave dirty. File: writes the file right away
        private void Commit(SaveData data, bool changed)
        {
            if (!changed) return;
            if (Live)
            {
                GameSave.MarkDirty();
                return;
            }
            try
            {
                fileStorage.Write(data.ToEnvelopeJson(fileVersion, true));
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Save Viewer", $"Không ghi được file:\n{e.Message}", "OK");
            }
        }

        private void RefreshFiles()
        {
            string current = files.Length > 0 && fileIndex < files.Length ? files[fileIndex] : null;
            files = FindSaveFiles();
            fileIndex = Mathf.Max(0, Array.IndexOf(files, current));
            edits.Clear();
            LoadFile();
        }

        private void LoadFile()
        {
            fileStorage = null;
            fileData = null;
            loadError = null;
            edits.Clear();
            if (files.Length == 0) return;

            string path = files[fileIndex];
            FileSaveStorage storage = CreateStorage(path);
            if (!storage.TryReadFile(path, out string json) || !SaveData.TryParseEnvelope(json, out fileData, out fileVersion))
            {
                loadError = $"Không đọc được {Path.GetFileName(path)}: file hỏng, hoặc mã hoá bằng EncryptKey riêng (xem trong Play mode).";
                fileData = null;
                return;
            }
            fileStorage = storage;
        }

        //Edit mode writes plain JSON, the editor always accepts plain files. The default key decrypts files written without a custom EncryptKey
        private static FileSaveStorage CreateStorage(string path) => new FileSaveStorage(path, false, SaveOptions.DefaultKey, true);

        //Files in persistentDataPath that look like GameSave files (encrypted magic, or a valid envelope)
        private static string[] FindSaveFiles()
        {
            string dir = Application.persistentDataPath;
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            List<string> result = new List<string>();
            foreach (string path in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(path);
                if (ext == ".bak" || ext == ".tmp" || ext == ".corrupt") continue;
                try
                {
                    if (new FileInfo(path).Length > MaxFileSize) continue;
                    if (SaveCrypto.HasMagic(File.ReadAllBytes(path)) || CreateStorage(path).TryReadFile(path, out _)) result.Add(path);
                }
                catch (Exception)
                {
                    //Unreadable file, not ours
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
#else
        void OnGUI()
        {
            EditorGUILayout.HelpBox("GameSave cần define USING_GAMESAVE: cài package com.unity.nuget.newtonsoft-json " +
                                    "(Package Manager → + → Add package by name), define sẽ tự bật.", MessageType.Warning);
        }
#endif
    }
}
