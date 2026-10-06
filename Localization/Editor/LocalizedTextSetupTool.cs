// Open via: Unity menu ▶ Raccoon ▶ Localization ▶ Setup LocalizedText in Scene

using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if USING_TMP
using TMPro;
#endif

namespace Raccoon.Localization
{
    /// <summary>
    /// Editor window that scans every GameObject in the active scene
    /// (including inactive ones) for <see cref="Text"/> or <see cref="TMP_Text"/>,
    /// lets you assign a localization Key inline, and batch-adds
    /// <see cref="LocalizedText"/> with the Key already set.
    ///
    /// Features
    /// ────────
    /// • Detects legacy UI Text and all TextMeshPro variants.
    /// • Inline Key text-field per row — type the key before applying.
    /// • "Auto-fill from text" button pre-fills keys from current display text.
    /// • Objects that already have LocalizedText show their current key (editable).
    /// • Click any row to ping + select in the scene hierarchy.
    /// • Filter by path / text / component type.
    /// • Full Undo support — Ctrl+Z rolls back every added component.
    /// • Marks the scene dirty so Unity prompts to save.
    /// </summary>
    public class LocalizedTextSetupTool : EditorWindow
    {
        // ─── Inner data model ─────────────────────────────────────────────────────

        private class Entry
        {
            public GameObject GameObject;
            public string ComponentType; // display name shown in the table
            public string CurrentText; // text on the component at scan time
            public bool AlreadyHas; // LocalizedText already present?
            public bool WillAdd; // checkbox: add on Apply?
            public string Key = ""; // localization key typed by the user
        }

        // ─── State ────────────────────────────────────────────────────────────────

        private List<Entry> _entries = new();
        private Vector2 _scroll;
        private string _filter = "";
        private bool _showExisting = true;

        // ─── Styles (lazy) ────────────────────────────────────────────────────────

        private GUIStyle _styleRowEven;
        private GUIStyle _styleRowOdd;
        private GUIStyle _stylePath;
        private GUIStyle _styleKeyField;
        private GUIStyle _styleTagHas;
        private GUIStyle _styleTagWill;
        private GUIStyle _styleTagSkip;
        private GUIStyle _styleTagNoKey;
        private bool _stylesReady;

        // ─── Column widths ────────────────────────────────────────────────────────

        private const float W_TOGGLE = 22f;
        private const float W_TYPE = 148f;
        private const float W_TEXT = 160f;
        private const float W_KEY = 164f;
        private const float W_BTN = 54f;
        private const float W_STATUS = 92f;

        // ═════════════════════════════════════════════════════════════════════════
        // Menu
        // ═════════════════════════════════════════════════════════════════════════

        [MenuItem("Raccoon/Localization/Setup LocalizedText in Scene")]
        private static void Open()
        {
            var win = GetWindow<LocalizedTextSetupTool>(true,
                "LocalizedText Setup", focus: true);
            win.minSize = new Vector2(840, 480);
            win.Scan();
            win.Show();
        }

        [MenuItem("Raccoon/Localization/Setup LocalizedText in Scene", validate = true)]
        private static bool CanOpen()
            => !Application.isPlaying && SceneManager.GetActiveScene().IsValid();

        // ═════════════════════════════════════════════════════════════════════════
        // Scan
        // ═════════════════════════════════════════════════════════════════════════

        private void Scan()
        {
            _entries.Clear();

            var stack = new Stack<GameObject>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                stack.Push(root);

            while (stack.Count > 0)
            {
                var go = stack.Pop();
                foreach (Transform child in go.transform)
                    stack.Push(child.gameObject);

                var existing = go.GetComponent<LocalizedText>();
                bool alreadyHas = existing != null;

                // Read current key via SerializedObject so we don't need a public property
                string existingKey = "";
                if (alreadyHas)
                {
                    var so = new SerializedObject(existing);
                    existingKey = so.FindProperty("key")?.stringValue ?? "";
                }

                // ── Legacy UI Text ─────────────────────────────────────────────
                var legacy = go.GetComponent<Text>();
                if (legacy != null)
                {
                    _entries.Add(new Entry
                    {
                        GameObject = go,
                        ComponentType = "Text (Legacy)",
                        CurrentText = legacy.text,
                        AlreadyHas = alreadyHas,
                        WillAdd = !alreadyHas,
                        Key = existingKey,
                    });
                    continue;
                }

#if USING_TMP
                // ── TextMeshProUGUI / TextMeshPro ──────────────────────────────
                var tmp = go.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    _entries.Add(new Entry
                    {
                        GameObject = go,
                        ComponentType = tmp.GetType().Name,
                        CurrentText = tmp.text,
                        AlreadyHas = alreadyHas,
                        WillAdd = !alreadyHas,
                        Key = existingKey,
                    });
                }
#endif
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Apply
        // ═════════════════════════════════════════════════════════════════════════

        private int Apply()
        {
            int count = 0;

            foreach (var e in _entries)
            {
                if (e.GameObject == null) continue;

                LocalizedText lt = e.GameObject.GetComponent<LocalizedText>();

                // Add component if needed
                if (!e.AlreadyHas && e.WillAdd)
                {
                    lt = Undo.AddComponent<LocalizedText>(e.GameObject);
                    e.AlreadyHas = true;
                    e.WillAdd = false;
                    count++;
                }

                // Write the key (works for both newly added AND pre-existing)
                if (lt != null && !string.IsNullOrEmpty(e.Key))
                {
                    var so = new SerializedObject(lt);
                    var prop = so.FindProperty("key");
                    if (prop != null)
                    {
                        Undo.RecordObject(lt, "Set Localization Key");
                        prop.stringValue = e.Key;
                        so.ApplyModifiedProperties();
                    }
                }
            }

            if (count > 0)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Debug.Log($"[LocalizedTextSetup] Added LocalizedText to {count} object(s).");
            }

            return count;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GUI
        // ═════════════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();
            DrawColumnHeaders();
            DrawList();
            DrawFooter();
        }

        // ── Toolbar ───────────────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("↺  Rescan Scene", EditorStyles.toolbarButton,
                    GUILayout.Width(118)))
                Scan();

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Filter:", GUILayout.Width(38));
            _filter = EditorGUILayout.TextField(_filter,
                EditorStyles.toolbarSearchField, GUILayout.Width(180));

            GUILayout.Space(6);
            _showExisting = GUILayout.Toggle(_showExisting, " Show existing",
                EditorStyles.toolbarButton, GUILayout.Width(110));

            GUILayout.Space(10);

            // Auto-fill keys from current display text (UPPER_SNAKE_CASE)
            if (GUILayout.Button("Auto-fill keys from text", EditorStyles.toolbarButton,
                    GUILayout.Width(160)))
                AutoFillKeys();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("All", EditorStyles.toolbarButton, GUILayout.Width(36)))
                foreach (var e in _entries)
                    if (!e.AlreadyHas)
                        e.WillAdd = true;
            if (GUILayout.Button("None", EditorStyles.toolbarButton, GUILayout.Width(40)))
                foreach (var e in _entries)
                    if (!e.AlreadyHas)
                        e.WillAdd = false;

            EditorGUILayout.EndHorizontal();
        }

        // ── Column headers ────────────────────────────────────────────────────────

        private void DrawColumnHeaders()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Space(W_TOGGLE + 4);
            GUILayout.Label("GameObject (hierarchy path)", EditorStyles.boldLabel,
                GUILayout.ExpandWidth(true));
            GUILayout.Label("Component", EditorStyles.boldLabel, GUILayout.Width(W_TYPE));
            GUILayout.Label("Text value", EditorStyles.boldLabel, GUILayout.Width(W_TEXT));
            GUILayout.Label("Key", EditorStyles.boldLabel, GUILayout.Width(W_KEY));
            GUILayout.Label("Update", EditorStyles.boldLabel, GUILayout.Width(W_BTN));
            GUILayout.Label("Status", EditorStyles.boldLabel, GUILayout.Width(W_STATUS));
            EditorGUILayout.EndHorizontal();
        }

        // ── Scrollable row list ───────────────────────────────────────────────────

        private void DrawList()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            int rowIdx = 0;

            foreach (var e in _entries)
            {
                if (!PassesFilter(e)) continue;
                if (!_showExisting && e.AlreadyHas) continue;

                var rowStyle = rowIdx++ % 2 == 0 ? _styleRowEven : _styleRowOdd;
                EditorGUILayout.BeginHorizontal(rowStyle, GUILayout.Height(22));

                // ── Checkbox ───────────────────────────────────────────────────
                EditorGUI.BeginDisabledGroup(e.AlreadyHas);
                bool toggled = EditorGUILayout.Toggle(e.WillAdd, GUILayout.Width(W_TOGGLE));
                if (toggled != e.WillAdd) e.WillAdd = toggled;
                EditorGUI.EndDisabledGroup();

                // ── Hierarchy path (click → ping) ──────────────────────────────
                if (GUILayout.Button(HierarchyPath(e.GameObject), _stylePath,
                        GUILayout.ExpandWidth(true)))
                {
                    Selection.activeGameObject = e.GameObject;
                    EditorGUIUtility.PingObject(e.GameObject);
                }

                // ── Component type ─────────────────────────────────────────────
                GUILayout.Label(e.ComponentType, EditorStyles.miniLabel,
                    GUILayout.Width(W_TYPE));

                // ── Current display text (truncated) ───────────────────────────
                string preview = (e.CurrentText ?? "").Replace("\n", "↵");
                if (preview.Length > 22) preview = preview[..22] + "…";
                GUILayout.Label(preview, EditorStyles.miniLabel, GUILayout.Width(W_TEXT));

                // ── Key input field ────────────────────────────────────────────
                DrawKeyField(e);

                // ── Update key button ──────────────────────────────────────────
                DrawUpdateButton(e);

                // ── Status badge ───────────────────────────────────────────────
                DrawStatusBadge(e);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Key text field ────────────────────────────────────────────────────────

        private void DrawKeyField(Entry e)
        {
            bool keyMissing = string.IsNullOrWhiteSpace(e.Key) && (e.WillAdd || e.AlreadyHas);

            // Tint background red when key is empty and the row will be applied
            if (keyMissing)
            {
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f, 0.55f);
                string edited = EditorGUILayout.TextField(e.Key, _styleKeyField,
                    GUILayout.Width(W_KEY));
                if (edited != e.Key)
                {
                    e.Key = edited.Trim();
                    // If this already has LocalizedText, push key change live
                    if (e.AlreadyHas) WriteKeyNow(e);
                }

                GUI.backgroundColor = prev;
            }
            else
            {
                string edited = EditorGUILayout.TextField(e.Key, _styleKeyField,
                    GUILayout.Width(W_KEY));
                if (edited != e.Key)
                {
                    e.Key = edited.Trim();
                    if (e.AlreadyHas) WriteKeyNow(e);
                }
            }
        }

        // ── Update button ─────────────────────────────────────────────────────────

        private void DrawUpdateButton(Entry e)
        {
            bool hasComponent = e.AlreadyHas || e.WillAdd;
            bool keyReady = !string.IsNullOrWhiteSpace(e.Key);

            // Disabled when: no component will exist, or key is blank
            EditorGUI.BeginDisabledGroup(!hasComponent || !keyReady);

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = (hasComponent && keyReady)
                ? new Color(0.30f, 0.70f, 0.40f) // green when actionable
                : new Color(0.40f, 0.40f, 0.40f); // grey when disabled

            if (GUILayout.Button("Apply", GUILayout.Width(W_BTN), GUILayout.Height(18)))
            {
                // If the component doesn't exist yet, add it first
                if (!e.AlreadyHas)
                {
                    Undo.AddComponent<LocalizedText>(e.GameObject);
                    e.AlreadyHas = true;
                    e.WillAdd = false;
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                }

                WriteKeyNow(e);
            }

            GUI.backgroundColor = prev;
            EditorGUI.EndDisabledGroup();
        }

        /// <summary>Immediately writes the key to an existing LocalizedText component.</summary>
        private static void WriteKeyNow(Entry e)
        {
            var lt = e.GameObject.GetComponent<LocalizedText>();
            if (lt == null) return;
            var so = new SerializedObject(lt);
            var prop = so.FindProperty("key");
            if (prop == null) return;
            Undo.RecordObject(lt, "Edit Localization Key");
            prop.stringValue = e.Key;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        // ── Status badge ──────────────────────────────────────────────────────────

        private void DrawStatusBadge(Entry e)
        {
            bool keyEmpty = string.IsNullOrWhiteSpace(e.Key);

            if (e.AlreadyHas)
            {
                GUILayout.Label("✔  has it", _styleTagHas, GUILayout.Width(W_STATUS));
            }
            else if (e.WillAdd && keyEmpty)
            {
                GUILayout.Label("⚠  need key", _styleTagNoKey, GUILayout.Width(W_STATUS));
            }
            else if (e.WillAdd)
            {
                GUILayout.Label("+  will add", _styleTagWill, GUILayout.Width(W_STATUS));
            }
            else
            {
                GUILayout.Label("—  skip", _styleTagSkip, GUILayout.Width(W_STATUS));
            }
        }

        // ── Footer ────────────────────────────────────────────────────────────────

        private void DrawFooter()
        {
            // Separator line
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 1f));
            EditorGUILayout.Space(4);

            int total = _entries.Count, already = 0, pending = 0, noKey = 0;
            foreach (var e in _entries)
            {
                if (e.AlreadyHas) already++;
                else if (e.WillAdd)
                {
                    pending++;
                    if (string.IsNullOrWhiteSpace(e.Key)) noKey++;
                }
            }

            EditorGUILayout.BeginHorizontal();

            // Summary label
            string summary = $"Found {total}   •   {already} already added   •   {pending} selected";
            if (noKey > 0)
                summary += $"   •   ⚠ {noKey} missing key";
            EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            // Warn but still allow apply even with empty keys (they can be set later)
            EditorGUI.BeginDisabledGroup(pending == 0);
            string btnLabel = noKey > 0
                ? $"Add {pending} object(s)  ⚠ {noKey} no key"
                : $"Add LocalizedText to {pending} object(s)";

            if (GUILayout.Button(btnLabel, GUILayout.Height(26), GUILayout.Width(270)))
            {
                bool proceed = noKey == 0 || EditorUtility.DisplayDialog(
                    "Missing Keys",
                    $"{noKey} selected object(s) have no Key set.\n\n" +
                    "They will get a LocalizedText component with an empty key — " +
                    "you can fill it in the Inspector later.\n\nProceed anyway?",
                    "Add anyway", "Cancel");

                if (proceed)
                {
                    int added = Apply();
                    Scan(); // refresh to reflect new state
                    EditorUtility.DisplayDialog(
                        "Done",
                        $"Added LocalizedText to {added} GameObject(s).\n\n" +
                        (noKey > 0
                            ? "Remember to set the Key on objects that still show ⚠ in the Inspector."
                            : "All keys were set successfully."),
                        "OK");
                }
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Helpers
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Pre-fills Key for every entry that has no key yet by converting
        /// the current display text to UPPER_SNAKE_CASE.
        /// e.g. "Play Game" → "PLAY_GAME"
        /// </summary>
        private void AutoFillKeys()
        {
            foreach (var e in _entries)
            {
                if (!string.IsNullOrWhiteSpace(e.Key)) continue; // don't overwrite existing
                if (string.IsNullOrWhiteSpace(e.CurrentText)) continue;

                e.Key = ToUpperSnakeCase(e.CurrentText);
                if (e.AlreadyHas) WriteKeyNow(e);
            }
        }

        private static string ToUpperSnakeCase(string text)
        {
            // Keep only letters, digits and spaces; collapse whitespace; uppercase
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
                else if (char.IsWhiteSpace(c) && sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
            }

            // Trim trailing underscores
            string result = sb.ToString().TrimEnd('_');
            return result.Length > 0 ? result : "KEY";
        }

        private bool PassesFilter(Entry e)
        {
            if (string.IsNullOrEmpty(_filter)) return true;
            string f = _filter.ToLowerInvariant();
            return HierarchyPath(e.GameObject).ToLowerInvariant().Contains(f)
                   || (e.CurrentText?.ToLowerInvariant().Contains(f) ?? false)
                   || e.ComponentType.ToLowerInvariant().Contains(f)
                   || (e.Key?.ToLowerInvariant().Contains(f) ?? false);
        }

        private static string HierarchyPath(GameObject go)
        {
            if (go == null) return "(null)";
            var parts = new List<string> { go.name };
            var t = go.transform.parent;
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }

            return string.Join("/", parts);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Style init
        // ═════════════════════════════════════════════════════════════════════════

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _styleRowEven = new GUIStyle
            {
                normal = { background = SolidTex(new Color(0.21f, 0.21f, 0.21f)) }
            };
            _styleRowOdd = new GUIStyle
            {
                normal = { background = SolidTex(new Color(0.245f, 0.245f, 0.245f)) }
            };
            _stylePath = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(2, 2, 0, 0),
            };
            _styleKeyField = new GUIStyle(EditorStyles.textField)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 2, 2),
            };
            _styleTagHas = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.35f, 0.88f, 0.48f) },
            };
            _styleTagWill = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.55f, 0.80f, 1.00f) },
            };
            _styleTagSkip = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.50f, 0.50f, 0.50f) },
            };
            _styleTagNoKey = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1.00f, 0.60f, 0.20f) },
            };
        }

        private static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
