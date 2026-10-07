// Data + storage for Raccoon Build Report.
//
// <project>/BuildReports/Last/<key>.json          snapshot of the latest build per key (always written, used for comparison)
// <project>/BuildReports/History/*.json           snapshots kept only when size changed more than the threshold
// <project>/UserSettings/RaccoonBuildReport.json  per-user settings
//
// key = platform + output type, e.g. "Android_apk" / "Android_aab", so APK and AAB are never compared.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Raccoon.GameKit
{
    [Serializable]
    internal class BuildReportSettings
    {
        public bool alwaysReport;
        public float thresholdMB = 1f;
        public bool analyzePackage = true;

        public long ThresholdBytes => (long)(Mathf.Max(0f, thresholdMB) * 1024 * 1024);
    }

    [Serializable]
    internal class SizeEntry
    {
        public string name;
        public string type;
        public long size;       // uncompressed (asset packed size / file size inside the package)
        public long compressed; // package entries only
    }

    [Serializable]
    internal class SizeChange
    {
        public string name;
        public string type;
        public long oldSize;
        public long newSize;

        public long Delta => newSize - oldSize;
    }

    [Serializable]
    internal class BuildSnapshot
    {
        public string key;
        public string platform;
        public string outputPath;
        public string time; // ISO 8601, local time
        public string unityVersion;
        public string appVersion;
        public double buildSeconds;
        public long outputSize;
        public long previousSize = -1;
        public string previousTime;
        public List<SizeEntry> assets = new();          // per source asset, sorted by size desc
        public List<SizeEntry> assetTypes = new();      // per object type
        public List<SizeEntry> packageGroups = new();   // APK/AAB content grouped (lib/<abi>, assets/bin/Data, dex...)
        public List<SizeEntry> packageEntries = new();  // biggest files inside APK/AAB
        public List<SizeChange> changes = new();        // asset diff vs previous build
        public List<SizeChange> packageChanges = new(); // package group diff vs previous build (compressed size)

        public bool HasPrevious => previousSize >= 0;
        public long Delta => HasPrevious ? outputSize - previousSize : 0;
        public DateTime Time => DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t) ? t : DateTime.MinValue;
    }

    internal class BuildReportRef
    {
        public string path;
        public string label;
        public bool isHistory;
    }

    internal static class BuildReportStorage
    {
        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        public static string Root => Path.Combine(ProjectRoot, "BuildReports");
        private static string LastDir => Path.Combine(Root, "Last");
        private static string HistoryDir => Path.Combine(Root, "History");
        private static string SettingsPath => Path.Combine(ProjectRoot, "UserSettings", "RaccoonBuildReport.json");

        public static BuildReportSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonUtility.FromJson<BuildReportSettings>(File.ReadAllText(SettingsPath)) ?? new BuildReportSettings();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Raccoon] Không đọc được {SettingsPath}: {e.Message}");
            }
            return new BuildReportSettings();
        }

        public static void SaveSettings(BuildReportSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
        }

        public static BuildSnapshot LoadLast(string key) => Load(Path.Combine(LastDir, key + ".json"));

        public static string SaveLast(BuildSnapshot snapshot)
        {
            string path = Path.Combine(LastDir, snapshot.key + ".json");
            Write(path, snapshot);
            return path;
        }

        // File name carries time, key and delta so the history list can be labelled without loading every file.
        public static string SaveHistory(BuildSnapshot snapshot)
        {
            string stamp = snapshot.Time.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(HistoryDir, $"{stamp}__{snapshot.key}__{snapshot.Delta}.json");
            Write(path, snapshot);
            return path;
        }

        public static BuildSnapshot Load(string path)
        {
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<BuildSnapshot>(File.ReadAllText(path)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Raccoon] Không đọc được report {path}: {e.Message}");
                return null;
            }
        }

        public static void Delete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        public static List<BuildReportRef> List()
        {
            var list = new List<BuildReportRef>();

            if (Directory.Exists(LastDir))
            {
                foreach (string path in Directory.GetFiles(LastDir, "*.json").OrderBy(p => p))
                    list.Add(new BuildReportRef { path = path, label = $"Build gần nhất · {Path.GetFileNameWithoutExtension(path)}" });
            }

            if (Directory.Exists(HistoryDir))
            {
                foreach (string path in Directory.GetFiles(HistoryDir, "*.json").OrderByDescending(p => p))
                    list.Add(new BuildReportRef { path = path, label = HistoryLabel(path), isHistory = true });
            }

            return list;
        }

        private static string HistoryLabel(string path)
        {
            string[] parts = Path.GetFileNameWithoutExtension(path).Split(new[] { "__" }, StringSplitOptions.None);
            if (parts.Length != 3
                || !DateTime.TryParseExact(parts[0], "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)
                || !long.TryParse(parts[2], out long delta))
                return "Lịch sử · " + Path.GetFileNameWithoutExtension(path);
            return $"Lịch sử · {time:dd-MM-yyyy HH:mm} · {parts[1]} · {FormatDelta(delta)}";
        }

        private static void Write(string path, BuildSnapshot snapshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(snapshot));
        }

        public static string FormatSize(long bytes)
        {
            double abs = Math.Abs((double)bytes);
            if (abs >= 1024 * 1024)
                return (bytes / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
            if (abs >= 1024)
                return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return bytes + " B";
        }

        public static string FormatDelta(long bytes) => (bytes > 0 ? "+" : bytes < 0 ? "-" : "±") + FormatSize(Math.Abs(bytes));
    }
}
