// Raccoon → Localization → Import Localization...
//
// Generates en.csv, vi.csv, ... for LocalizationManager from either:
//   • an Excel workbook (.xlsx) — e.g. Docs/LocalizationTemplate.xlsx
//   • a Google Sheet shared as "Anyone with the link can view"

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Raccoon.Localization
{
    internal class LocalizationImportWindow : EditorWindow
    {
        private enum SourceType
        {
            ExcelFile,
            GoogleSheet,
        }

        private const string DefaultOutputFolder = "Assets/GameKit/Resources/Localization";
        private const string DefaultSheetName = "Localization";

        private static readonly Regex SheetIdPattern = new(@"/spreadsheets/d/([a-zA-Z0-9_-]+)");
        private static readonly Regex GidPattern = new(@"[#?&]gid=(\d+)");

        private SourceType _source;
        private string _excelPath;
        private string _sheetName;
        private string _sheetUrl;
        private string _outputFolder;
        private bool _fallbackToEnglish;

        private List<string> _sheetNames = new();
        private string _sheetNamesFor;

        private UnityWebRequest _request;
        private string _status;
        private MessageType _statusType;

        [MenuItem("Raccoon/Localization/Import Localization...")]
        private static void Open()
        {
            var window = GetWindow<LocalizationImportWindow>("Localization Import");
            window.minSize = new Vector2(460, 260);
        }

        // ─── Settings (per project) ───────────────────────────────────────────────

        private static string PrefKey(string name) => $"Raccoon.Localization.Import.{PlayerSettings.productGUID}.{name}";

        private void OnEnable()
        {
            _source = (SourceType)EditorPrefs.GetInt(PrefKey("Source"), 0);
            _excelPath = EditorPrefs.GetString(PrefKey("ExcelPath"), "");
            _sheetName = EditorPrefs.GetString(PrefKey("SheetName"), DefaultSheetName);
            _sheetUrl = EditorPrefs.GetString(PrefKey("SheetUrl"), "");
            _outputFolder = EditorPrefs.GetString(PrefKey("OutputFolder"), DefaultOutputFolder);
            _fallbackToEnglish = EditorPrefs.GetBool(PrefKey("FallbackToEnglish"), true);
        }

        private void SaveSettings()
        {
            EditorPrefs.SetInt(PrefKey("Source"), (int)_source);
            EditorPrefs.SetString(PrefKey("ExcelPath"), _excelPath);
            EditorPrefs.SetString(PrefKey("SheetName"), _sheetName);
            EditorPrefs.SetString(PrefKey("SheetUrl"), _sheetUrl);
            EditorPrefs.SetString(PrefKey("OutputFolder"), _outputFolder);
            EditorPrefs.SetBool(PrefKey("FallbackToEnglish"), _fallbackToEnglish);
        }

        private void OnDisable()
        {
            CancelDownload();
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space();
            _source = (SourceType)EditorGUILayout.EnumPopup("Source", _source);
            EditorGUILayout.Space();

            if (_source == SourceType.ExcelFile)
                DrawExcelSource();
            else
                DrawGoogleSheetSource();

            EditorGUILayout.Space();
            DrawOutput();

            if (EditorGUI.EndChangeCheck())
                SaveSettings();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_request != null))
            {
                if (GUILayout.Button(_request != null ? "Downloading..." : "Import", GUILayout.Height(30)))
                    Import();
            }

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _statusType);
        }

        private void DrawExcelSource()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _excelPath = EditorGUILayout.TextField("Excel File", _excelPath);
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    string dir = File.Exists(_excelPath) ? Path.GetDirectoryName(_excelPath) : Application.dataPath;
                    string picked = EditorUtility.OpenFilePanel("Select localization workbook", dir, "xlsx");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        _excelPath = ToProjectRelative(picked);
                        GUI.changed = true;
                    }
                }
            }

            RefreshSheetNames();
            if (_sheetNames.Count > 0)
            {
                int index = Mathf.Max(0, _sheetNames.IndexOf(_sheetName));
                _sheetName = _sheetNames[EditorGUILayout.Popup("Sheet", index, _sheetNames.ToArray())];
            }
            else
            {
                _sheetName = EditorGUILayout.TextField("Sheet", _sheetName);
            }
        }

        private void DrawGoogleSheetSource()
        {
            _sheetUrl = EditorGUILayout.TextField("Sheet URL", _sheetUrl);
            EditorGUILayout.HelpBox(
                "Paste the browser URL of the tab to import (the #gid=... part selects the tab).\n" +
                "Share → General access → \"Anyone with the link\" (Viewer).",
                MessageType.None);
        }

        private void DrawOutput()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Select output folder", _outputFolder, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        _outputFolder = ToProjectRelative(picked);
                        GUI.changed = true;
                    }
                }
            }

            if (!_outputFolder.Replace('\\', '/').Contains("/Resources/"))
                EditorGUILayout.HelpBox("Output folder should be inside a Resources folder so LocalizationManager can load it.", MessageType.Warning);

            _fallbackToEnglish = EditorGUILayout.Toggle(
                new GUIContent("Fallback To English", "Fill empty translations with the English value instead of leaving the key missing."),
                _fallbackToEnglish);
        }

        private void RefreshSheetNames()
        {
            if (_sheetNamesFor == _excelPath) return;
            _sheetNamesFor = _excelPath;
            _sheetNames = new List<string>();

            if (!File.Exists(_excelPath)) return;
            try
            {
                _sheetNames = LocalizationSheetReader.GetXlsxSheetNames(_excelPath);
                if (!_sheetNames.Contains(_sheetName) && _sheetNames.Count > 0)
                    _sheetName = _sheetNames.Contains(DefaultSheetName) ? DefaultSheetName : _sheetNames[0];
            }
            catch (Exception e)
            {
                SetStatus($"Could not read workbook: {e.Message}", MessageType.Error);
            }
        }

        // ─── Import ───────────────────────────────────────────────────────────────

        private void Import()
        {
            if (_source == SourceType.ExcelFile)
            {
                if (!File.Exists(_excelPath))
                {
                    SetStatus($"File not found: {_excelPath}", MessageType.Error);
                    return;
                }

                Run(() => LocalizationSheetReader.ReadXlsx(_excelPath, _sheetName));
            }
            else
            {
                StartDownload();
            }
        }

        private void StartDownload()
        {
            var idMatch = SheetIdPattern.Match(_sheetUrl ?? "");
            if (!idMatch.Success)
            {
                SetStatus("Invalid Google Sheet URL — expected https://docs.google.com/spreadsheets/d/<id>/...", MessageType.Error);
                return;
            }

            var gidMatch = GidPattern.Match(_sheetUrl);
            string gid = gidMatch.Success ? gidMatch.Groups[1].Value : "0";
            string url = $"https://docs.google.com/spreadsheets/d/{idMatch.Groups[1].Value}/export?format=csv&gid={gid}";

            _request = UnityWebRequest.Get(url);
            _request.SendWebRequest();
            EditorApplication.update += PollDownload;
            SetStatus("Downloading...", MessageType.Info);
        }

        private void PollDownload()
        {
            if (_request == null || !_request.isDone) return;
            EditorApplication.update -= PollDownload;

            var request = _request;
            _request = null;

            using (request)
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    SetStatus($"Download failed: {request.error}", MessageType.Error);
                    return;
                }

                // A private sheet redirects to the Google sign-in page instead of returning CSV
                string contentType = request.GetResponseHeader("Content-Type") ?? "";
                if (contentType.Contains("text/html"))
                {
                    SetStatus("Google returned a web page instead of CSV — share the sheet as \"Anyone with the link can view\".", MessageType.Error);
                    return;
                }

                string csv = request.downloadHandler.text;
                Run(() => LocalizationSheetReader.ParseCsv(csv));
            }
        }

        private void CancelDownload()
        {
            if (_request == null) return;
            EditorApplication.update -= PollDownload;
            _request.Abort();
            _request.Dispose();
            _request = null;
        }

        private void Run(Func<List<List<string>>> readRows)
        {
            try
            {
                var result = LocalizationCsvExporter.Export(readRows(), _outputFolder, _fallbackToEnglish);
                AssetDatabase.Refresh();

                string message = $"Imported to {_outputFolder}:\n  " + string.Join("\n  ", result.Files);
                if (result.Warnings.Count > 0)
                    message += "\n\nWarnings:\n  " + string.Join("\n  ", result.Warnings);

                SetStatus(message, result.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info);
                Debug.Log($"[Localization] {message}");
            }
            catch (Exception e)
            {
                SetStatus($"Import failed: {e.Message}", MessageType.Error);
                Debug.LogException(e);
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            Repaint();
        }

        private static string ToProjectRelative(string absolutePath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/";
            absolutePath = absolutePath.Replace('\\', '/');
            return absolutePath.StartsWith(projectRoot) ? absolutePath.Substring(projectRoot.Length) : absolutePath;
        }
    }
}
