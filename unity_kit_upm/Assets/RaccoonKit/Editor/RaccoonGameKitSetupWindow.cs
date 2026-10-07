// Raccoon → GameKit Setup...
//
// Toggles the kit's Scripting Define Symbols (Player Settings) for the chosen build targets.
// Each define shows whether its SDK is detected in the project — enabling a define whose SDK
// is missing will break compilation.
// Defines also driven by asmdef versionDefines (UPM packages) work without this window, but a
// global define is still needed when the SDK was imported as a .unitypackage.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Raccoon.GameKit
{
    internal class RaccoonGameKitSetupWindow : EditorWindow
    {
        private readonly struct DefineInfo
        {
            public readonly string Symbol;
            public readonly string Module;
            public readonly string Description;
            public readonly string DetectType; // full type name proving the SDK is present; null = no SDK needed

            public DefineInfo(string symbol, string module, string description, string detectType)
            {
                Symbol = symbol;
                Module = module;
                Description = description;
                DetectType = detectType;
            }
        }

        private static readonly DefineInfo[] KitDefines =
        {
            new("USING_FIREBASE", "GameService", "Firebase Analytics + Crashlytics", "Firebase.FirebaseApp"),
            new("USING_REMOTECONFIG", "GameService", "Firebase Remote Config (cần USING_FIREBASE)", "Firebase.RemoteConfig.FirebaseRemoteConfig"),
            new("USING_INAPPREVIEW", "GameService", "Google Play In-App Review (Android)", "Google.Play.Review.ReviewManager"),
            new("USING_PURCHASE", "Purchase", "Unity IAP (com.unity.purchasing)", "UnityEngine.Purchasing.UnityPurchasing"),
            new("USING_HAPTIC", "Haptic", "Rung native Android/iOS", null),
            new("USING_TMP", "Localization", "TextMeshPro", "TMPro.TMP_Text"),
            new("USING_GAMESAVE", "Save", "GameSave (Newtonsoft JSON com.unity.nuget.newtonsoft-json)", "Newtonsoft.Json.JsonConvert"),
        };

        private static readonly (NamedBuildTarget target, string label)[] Targets =
        {
            (NamedBuildTarget.Android, "Android"),
            (NamedBuildTarget.iOS, "iOS"),
            (NamedBuildTarget.Standalone, "Standalone"),
        };

        private const string TargetPrefKey = "Raccoon.GameKit.Setup.Target.";

        private readonly bool[] targetEnabled = new bool[Targets.Length];
        private readonly Dictionary<string, bool> sdkDetected = new();
        private HashSet<string> pending; // edited define set, applied on "Apply"
        private string customDefine = "";
        private Vector2 scroll;

        [MenuItem("Raccoon/GameKit Setup...", priority = 0)]
        private static void Open()
        {
            var window = GetWindow<RaccoonGameKitSetupWindow>("Raccoon Game Kit Setup");
            window.minSize = new Vector2(480, 420);
            window.Show();
        }

        private void OnEnable()
        {
            for (int i = 0; i < Targets.Length; i++)
                targetEnabled[i] = EditorPrefs.GetBool(TargetPrefKey + Targets[i].label, i < 2); // Android + iOS by default
            DetectSdks();
            Reload();
        }

        private void OnFocus()
        {
            DetectSdks();
            if (!HasChanges())
                Reload();
        }

        private void DetectSdks()
        {
            sdkDetected.Clear();
            foreach (DefineInfo info in KitDefines)
                sdkDetected[info.Symbol] = info.DetectType == null || TypeExists(info.DetectType);
        }

        private static bool TypeExists(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType(fullName, false) != null);
        }

        // Union of the selected targets' defines; a define counts as "on" if any selected target has it.
        private HashSet<string> ReadDefines()
        {
            var set = new HashSet<string>();
            for (int i = 0; i < Targets.Length; i++)
            {
                if (!targetEnabled[i])
                    continue;
                PlayerSettings.GetScriptingDefineSymbols(Targets[i].target, out string[] defines);
                set.UnionWith(defines);
            }
            return set;
        }

        private void Reload()
        {
            pending = ReadDefines();
        }

        private bool HasChanges()
        {
            return pending != null && !pending.SetEquals(ReadDefines());
        }

        private void OnGUI()
        {
            pending ??= ReadDefines();

            EditorGUILayout.Space();
            DrawTargets();
            EditorGUILayout.Space();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawKitDefines();
            EditorGUILayout.Space();
            DrawOtherDefines();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            DrawFooter();
            EditorGUILayout.Space();
        }

        private void DrawTargets()
        {
            EditorGUILayout.LabelField("Build Targets", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < Targets.Length; i++)
            {
                bool value = EditorGUILayout.ToggleLeft(Targets[i].label, targetEnabled[i], GUILayout.Width(110));
                if (value == targetEnabled[i])
                    continue;
                targetEnabled[i] = value;
                EditorPrefs.SetBool(TargetPrefKey + Targets[i].label, value);
                Reload();
            }
            EditorGUILayout.EndHorizontal();

            if (!targetEnabled.Any(t => t))
                EditorGUILayout.HelpBox("Chọn ít nhất một build target.", MessageType.Warning);
        }

        private void DrawKitDefines()
        {
            EditorGUILayout.LabelField("Raccoon Kit Defines", EditorStyles.boldLabel);

            foreach (DefineInfo info in KitDefines)
            {
                bool detected = sdkDetected[info.Symbol];
                bool on = pending.Contains(info.Symbol);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                bool value = EditorGUILayout.ToggleLeft(info.Symbol, on, EditorStyles.boldLabel, GUILayout.Width(170));
                EditorGUILayout.LabelField($"{info.Module} — {info.Description}", EditorStyles.wordWrappedMiniLabel);
                GUILayout.FlexibleSpace();
                Color prevColor = GUI.color;
                GUI.color = detected ? new Color(0.4f, 0.9f, 0.4f) : new Color(1f, 0.6f, 0.3f);
                GUILayout.Label(info.DetectType == null ? "built-in" : detected ? "SDK ✓" : "No SDK", EditorStyles.miniBoldLabel, GUILayout.Width(55));
                GUI.color = prevColor;
                EditorGUILayout.EndHorizontal();

                if (value != on)
                    SetPending(info.Symbol, value);

                if (value && !detected)
                    EditorGUILayout.HelpBox($"Chưa tìm thấy SDK cho {info.Symbol} — bật define này sẽ gây lỗi compile.", MessageType.Warning);
            }

            if (pending.Contains("USING_REMOTECONFIG") && !pending.Contains("USING_FIREBASE"))
                EditorGUILayout.HelpBox("USING_REMOTECONFIG chỉ có tác dụng khi bật cùng USING_FIREBASE.", MessageType.Info);
        }

        private void DrawOtherDefines()
        {
            EditorGUILayout.LabelField("Other Defines", EditorStyles.boldLabel);

            var kitSymbols = new HashSet<string>(KitDefines.Select(d => d.Symbol));
            string toRemove = null;
            foreach (string symbol in pending.Where(s => !kitSymbols.Contains(s)).OrderBy(s => s))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(symbol);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    toRemove = symbol;
                EditorGUILayout.EndHorizontal();
            }
            if (toRemove != null)
                SetPending(toRemove, false);

            EditorGUILayout.BeginHorizontal();
            customDefine = EditorGUILayout.TextField(customDefine);
            string symbolToAdd = customDefine.Trim();
            using (new EditorGUI.DisabledScope(!IsValidSymbol(symbolToAdd) || pending.Contains(symbolToAdd)))
            {
                if (GUILayout.Button("Add", GUILayout.Width(70)))
                {
                    SetPending(symbolToAdd, true);
                    customDefine = "";
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (symbolToAdd.Length > 0 && !IsValidSymbol(symbolToAdd))
                EditorGUILayout.HelpBox("Define chỉ gồm chữ, số, '_' và không bắt đầu bằng số.", MessageType.Error);
        }

        private void DrawFooter()
        {
            bool changed = HasChanges();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!changed))
            {
                if (GUILayout.Button("Revert", GUILayout.Height(28)))
                    Reload();
            }
            using (new EditorGUI.DisabledScope(!changed || !targetEnabled.Any(t => t)))
            {
                if (GUILayout.Button(changed ? "Apply (recompile)" : "Apply", GUILayout.Height(28)))
                    Apply();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void SetPending(string symbol, bool on)
        {
            if (on)
                pending.Add(symbol);
            else
                pending.Remove(symbol);
        }

        // Adds/removes only what changed, so defines that differ between targets are kept per target.
        private void Apply()
        {
            HashSet<string> current = ReadDefines();
            string[] added = pending.Except(current).ToArray();
            string[] removed = current.Except(pending).ToArray();

            for (int i = 0; i < Targets.Length; i++)
            {
                if (!targetEnabled[i])
                    continue;
                NamedBuildTarget target = Targets[i].target;
                PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
                List<string> list = defines.Where(d => !removed.Contains(d)).ToList();
                list.AddRange(added.Where(d => !list.Contains(d)));
                PlayerSettings.SetScriptingDefineSymbols(target, list.ToArray());
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Raccoon] Defines updated. Added: [{string.Join(", ", added)}] Removed: [{string.Join(", ", removed)}]");
            Reload();
        }

        private static bool IsValidSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol) || char.IsDigit(symbol[0]))
                return false;
            return symbol.All(c => char.IsLetterOrDigit(c) || c == '_');
        }
    }
}
