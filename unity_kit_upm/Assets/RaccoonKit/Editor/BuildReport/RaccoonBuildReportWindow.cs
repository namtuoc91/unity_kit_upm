// Raccoon → Build Report...
//
// Shows build snapshots written by RaccoonBuildReporter: total size + delta vs previous build,
// heaviest assets, asset changes, and APK/AAB contents (real compressed size per group / file).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Raccoon.GameKit
{
    internal class RaccoonBuildReportWindow : EditorWindow
    {
        private enum Tab { Overview, Assets, Changes, Package }

        private static readonly string[] TabNames = { "Tổng quan", "Assets", "Thay đổi", "APK / AAB" };
        private const int MaxRows = 300;

        private static readonly Color Grow = new(1f, 0.55f, 0.45f);
        private static readonly Color Shrink = new(0.45f, 0.9f, 0.45f);

        private BuildReportSettings settings;
        private List<BuildReportRef> reports = new();
        private int selected;
        private string currentPath;
        private BuildSnapshot current;
        private Tab tab;
        private string filter = "";
        private Vector2 scroll;

        [MenuItem("Raccoon/Build Report...", priority = 1)]
        private static void OpenFromMenu() => Open(null);

        public static void Open(string path)
        {
            var window = GetWindow<RaccoonBuildReportWindow>("Raccoon Build Report");
            window.minSize = new Vector2(560, 440);
            window.Refresh(path);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            settings = BuildReportStorage.LoadSettings();
            Refresh(currentPath);
        }

        private void OnFocus() => Refresh(currentPath);

        private void Refresh(string selectPath)
        {
            reports = BuildReportStorage.List();
            int index = selectPath == null ? -1 : reports.FindIndex(r => r.path == selectPath);
            Select(index >= 0 ? index : Mathf.Clamp(selected, 0, Mathf.Max(0, reports.Count - 1)));
        }

        private void Select(int index)
        {
            selected = index;
            string path = index >= 0 && index < reports.Count ? reports[index].path : null;
            if (path == currentPath && current != null)
                return;
            currentPath = path;
            current = path != null ? BuildReportStorage.Load(path) : null;
            scroll = Vector2.zero;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            DrawSettings();
            EditorGUILayout.Space();
            DrawSelector();

            if (current == null)
            {
                EditorGUILayout.HelpBox("Chưa có report. Build APK/AAB một lần để tạo dữ liệu.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            tab = (Tab)GUILayout.Toolbar((int)tab, TabNames, GUILayout.Height(24));
            EditorGUILayout.Space();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case Tab.Overview: DrawOverview(); break;
                case Tab.Assets: DrawAssets(); break;
                case Tab.Changes: DrawChanges(); break;
                case Tab.Package: DrawPackage(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Cài đặt", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();

            settings.alwaysReport = EditorGUILayout.ToggleLeft("Luôn mở report sau mỗi lần build", settings.alwaysReport);
            settings.thresholdMB = Mathf.Max(0.1f, EditorGUILayout.FloatField("Ngưỡng chênh lệch (MB)", settings.thresholdMB));
            settings.analyzePackage = EditorGUILayout.ToggleLeft("Phân tích nội dung APK / AAB (size sau nén từng phần)", settings.analyzePackage);

            if (EditorGUI.EndChangeCheck())
                BuildReportStorage.SaveSettings(settings);

            string mb = settings.thresholdMB.ToString("0.##", CultureInfo.InvariantCulture);
            EditorGUILayout.LabelField(settings.alwaysReport
                    ? $"Bật: mở report sau mọi build. Lịch sử chỉ lưu khi size lệch > {mb} MB so với build trước."
                    : $"Tắt: chỉ tự mở report + lưu lịch sử khi size lệch > {mb} MB so với build trước.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawSelector()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(reports.Count == 0))
            {
                int index = EditorGUILayout.Popup(selected, reports.Select(r => r.label).ToArray());
                if (index != selected)
                    Select(index);
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(64)))
            {
                current = null;
                Refresh(currentPath);
            }
            using (new EditorGUI.DisabledScope(current == null))
            {
                if (GUILayout.Button("CSV", GUILayout.Width(44)))
                    ExportCsv();
            }
            using (new EditorGUI.DisabledScope(selected < 0 || selected >= reports.Count || !reports[selected].isHistory))
            {
                if (GUILayout.Button("Xoá", GUILayout.Width(44))
                    && EditorUtility.DisplayDialog("Xoá report", $"Xoá {Path.GetFileName(currentPath)}?", "Xoá", "Huỷ"))
                {
                    BuildReportStorage.Delete(currentPath);
                    current = null;
                    currentPath = null;
                    Refresh(null);
                }
            }
            if (GUILayout.Button("Thư mục", GUILayout.Width(64)))
            {
                Directory.CreateDirectory(BuildReportStorage.Root);
                EditorUtility.RevealInFinder(BuildReportStorage.Root);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOverview()
        {
            BuildSnapshot s = current;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(BuildReportStorage.FormatSize(s.outputSize), new GUIStyle(EditorStyles.boldLabel) { fontSize = 20 }, GUILayout.Height(28));
            if (s.HasPrevious)
            {
                Color prev = GUI.color;
                GUI.color = s.Delta > 0 ? Grow : s.Delta < 0 ? Shrink : prev;
                EditorGUILayout.LabelField($"{BuildReportStorage.FormatDelta(s.Delta)} so với build trước ({BuildReportStorage.FormatSize(s.previousSize)}, {FormatTime(s.previousTime)})", EditorStyles.boldLabel);
                GUI.color = prev;
            }
            else
            {
                EditorGUILayout.LabelField("Build đầu tiên — chưa có để so sánh.", EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(4);
            Info("Key", s.key);
            Info("Thời gian", FormatTime(s.time));
            Info("Version", s.appVersion);
            Info("Unity", s.unityVersion);
            Info("Thời gian build", $"{s.buildSeconds:0}s");
            Info("Output", s.outputPath);
            EditorGUILayout.EndVertical();

            if (s.key.EndsWith("_aab"))
                EditorGUILayout.HelpBox("AAB chứa mọi ABI + resource; size người dùng tải về (APK từ Google Play) sẽ nhỏ hơn.", MessageType.Info);

            if (s.packageGroups.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Thành phần APK / AAB (sau nén)", EditorStyles.boldLabel);
                long total = s.packageGroups.Sum(g => g.compressed);
                foreach (SizeEntry g in s.packageGroups.Take(12))
                    BarRow(g.name, g.compressed, total);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Assets theo loại (trước nén)", EditorStyles.boldLabel);
            long assetTotal = s.assetTypes.Sum(t => t.size);
            foreach (SizeEntry t in s.assetTypes.Take(12))
                BarRow(t.name, t.size, assetTotal);
        }

        private void DrawAssets()
        {
            EditorGUILayout.HelpBox("Size do Unity serialize cho từng asset (trước nén). Không gồm code native / dex / plugin — xem tab APK / AAB.", MessageType.None);
            filter = EditorGUILayout.TextField("Lọc (path / type)", filter);

            List<SizeEntry> rows = current.assets.Where(a => Match(a.name, a.type)).ToList();
            long total = current.assets.Sum(a => a.size);

            Header(("Size", 80), ("%", 44), ("Type", 110), ("Path", 0));
            foreach (SizeEntry a in rows.Take(MaxRows))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(BuildReportStorage.FormatSize(a.size), GUILayout.Width(80));
                GUILayout.Label(Percent(a.size, total), GUILayout.Width(44));
                GUILayout.Label(a.type, GUILayout.Width(110));
                AssetLink(a.name);
                EditorGUILayout.EndHorizontal();
            }
            MoreRows(rows.Count);
        }

        private void DrawChanges()
        {
            if (!current.HasPrevious)
            {
                EditorGUILayout.HelpBox("Chưa có build trước để so sánh.", MessageType.Info);
                return;
            }

            if (current.packageChanges.Count > 0)
            {
                EditorGUILayout.LabelField("APK / AAB theo nhóm (sau nén)", EditorStyles.boldLabel);
                Header(("Chênh lệch", 90), ("Trước → Sau", 170), ("Nhóm", 0));
                foreach (SizeChange c in current.packageChanges)
                    ChangeRow(c, false);
                EditorGUILayout.Space();
            }

            EditorGUILayout.LabelField("Assets (trước nén)", EditorStyles.boldLabel);
            filter = EditorGUILayout.TextField("Lọc (path / type)", filter);
            List<SizeChange> rows = current.changes.Where(c => Match(c.name, c.type)).ToList();
            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField("Không có asset nào thay đổi.", EditorStyles.miniLabel);
                return;
            }
            Header(("Chênh lệch", 90), ("Trước → Sau", 170), ("Path", 0));
            foreach (SizeChange c in rows.Take(MaxRows))
                ChangeRow(c, true);
            MoreRows(rows.Count);
        }

        private void DrawPackage()
        {
            if (current.packageGroups.Count == 0)
            {
                EditorGUILayout.HelpBox(settings.analyzePackage
                    ? "Report này không có dữ liệu APK / AAB (output không phải .apk / .aab)."
                    : "Đang tắt \"Phân tích nội dung APK / AAB\". Bật lên rồi build lại.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Theo nhóm", EditorStyles.boldLabel);
            long total = current.packageGroups.Sum(g => g.compressed);
            Header(("Sau nén", 80), ("%", 44), ("Gốc", 80), ("Nhóm", 0));
            foreach (SizeEntry g in current.packageGroups)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(BuildReportStorage.FormatSize(g.compressed), GUILayout.Width(80));
                GUILayout.Label(Percent(g.compressed, total), GUILayout.Width(44));
                GUILayout.Label(BuildReportStorage.FormatSize(g.size), GUILayout.Width(80));
                GUILayout.Label(g.name);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"File lớn nhất (top {current.packageEntries.Count})", EditorStyles.boldLabel);
            filter = EditorGUILayout.TextField("Lọc", filter);
            List<SizeEntry> rows = current.packageEntries.Where(e => Match(e.name, e.type)).ToList();
            Header(("Sau nén", 80), ("Gốc", 80), ("File", 0));
            foreach (SizeEntry e in rows.Take(MaxRows))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(BuildReportStorage.FormatSize(e.compressed), GUILayout.Width(80));
                GUILayout.Label(BuildReportStorage.FormatSize(e.size), GUILayout.Width(80));
                GUILayout.Label(e.name);
                EditorGUILayout.EndHorizontal();
            }
        }

        private static void Info(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, EditorStyles.miniBoldLabel, GUILayout.Width(100));
            EditorGUILayout.SelectableLabel(value ?? "-", EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private static void BarRow(string label, long size, long total)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2);
            float ratio = total > 0 ? (float)size / total : 0f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1, rect.width * ratio, rect.height - 2), new Color(0.3f, 0.55f, 0.9f, 0.35f));
            GUI.Label(new Rect(rect.x + 4, rect.y, rect.width - 160, rect.height), label);
            GUI.Label(new Rect(rect.xMax - 150, rect.y, 150, rect.height), $"{BuildReportStorage.FormatSize(size)}   {Percent(size, total)}", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight });
        }

        private static void ChangeRow(SizeChange c, bool isAsset)
        {
            EditorGUILayout.BeginHorizontal();
            Color prev = GUI.color;
            GUI.color = c.Delta > 0 ? Grow : Shrink;
            GUILayout.Label(BuildReportStorage.FormatDelta(c.Delta), EditorStyles.boldLabel, GUILayout.Width(90));
            GUI.color = prev;
            string before = c.oldSize == 0 ? "mới" : BuildReportStorage.FormatSize(c.oldSize);
            string after = c.newSize == 0 ? "đã xoá" : BuildReportStorage.FormatSize(c.newSize);
            GUILayout.Label($"{before} → {after}", GUILayout.Width(170));
            if (isAsset)
                AssetLink(c.name);
            else
                GUILayout.Label(c.name);
            EditorGUILayout.EndHorizontal();
        }

        private static void AssetLink(string path)
        {
            if (!GUILayout.Button(new GUIContent(path, path), EditorStyles.label))
                return;
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null)
                EditorGUIUtility.PingObject(asset);
        }

        private static void Header(params (string label, float width)[] columns)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            foreach ((string label, float width) in columns)
            {
                if (width > 0)
                    GUILayout.Label(label, EditorStyles.miniBoldLabel, GUILayout.Width(width));
                else
                    GUILayout.Label(label, EditorStyles.miniBoldLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void MoreRows(int count)
        {
            if (count > MaxRows)
                EditorGUILayout.LabelField($"… còn {count - MaxRows} dòng, dùng ô Lọc hoặc Export CSV để xem hết.", EditorStyles.miniLabel);
        }

        private bool Match(string name, string type)
        {
            return string.IsNullOrEmpty(filter)
                   || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                   || (type != null && type.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string Percent(long size, long total) => total > 0 ? (100.0 * size / total).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";

        private static string FormatTime(string iso)
        {
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t) ? t.ToString("dd-MM-yyyy HH:mm:ss") : iso ?? "-";
        }

        private void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel("Export Build Report", "", $"BuildReport_{current.key}_{current.Time:yyyyMMdd_HHmmss}.csv", "csv");
            if (string.IsNullOrEmpty(path))
                return;

            var sb = new StringBuilder();
            sb.AppendLine("section,name,type,size,compressed_or_old,new");
            sb.AppendLine($"summary,{Csv(current.key)},{Csv(current.outputPath)},{current.outputSize},{current.previousSize},");
            foreach (SizeEntry e in current.packageGroups)
                sb.AppendLine($"package_group,{Csv(e.name)},,{e.size},{e.compressed},");
            foreach (SizeEntry e in current.packageEntries)
                sb.AppendLine($"package_file,{Csv(e.name)},{Csv(e.type)},{e.size},{e.compressed},");
            foreach (SizeEntry e in current.assets)
                sb.AppendLine($"asset,{Csv(e.name)},{Csv(e.type)},{e.size},,");
            foreach (SizeChange c in current.changes)
                sb.AppendLine($"asset_change,{Csv(c.name)},{Csv(c.type)},{c.Delta},{c.oldSize},{c.newSize}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            EditorUtility.RevealInFinder(path);
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
