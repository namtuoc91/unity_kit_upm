// Runs after every player build and snapshots its size.
//
// - Always: compare with the previous build of the same key and overwrite BuildReports/Last/<key>.json.
// - Size changed more than the threshold: also save to BuildReports/History and open the report window.
// - "Always report" on: open the report window after every build.
//
// CI / custom build scripts can call RaccoonBuildReporter.Process(report) with the BuildReport
// returned by BuildPipeline.BuildPlayer — the window is never opened in batch mode.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Raccoon.GameKit
{
    internal class RaccoonBuildReporter : IPostprocessBuildWithReport
    {
        private const int MaxPackageEntries = 200;
        private const int MaxChanges = 500;

        private static string lastProcessedGuid; // OnPostprocessBuild + an explicit Process() call must not run twice

        public int callbackOrder => int.MaxValue; // after other post-processors touched the output

        public void OnPostprocessBuild(BuildReport report)
        {
            try
            {
                Process(report);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Raccoon] Build Report lỗi: {e}");
            }
        }

        /// <summary>Snapshot a finished build. Returns the history file path when one was saved, otherwise null.</summary>
        public static string Process(BuildReport report)
        {
            BuildSummary summary = report.summary;
            string guid = summary.guid.ToString();
            if (guid == lastProcessedGuid)
                return null;
            lastProcessedGuid = guid;

            string output = summary.outputPath;
            long size = GetOutputSize(output);
            if (size <= 0)
            {
                Debug.LogWarning($"[Raccoon] Build Report: không tìm thấy output '{output}', bỏ qua.");
                return null;
            }

            BuildReportSettings settings = BuildReportStorage.LoadSettings();
            var snapshot = new BuildSnapshot
            {
                key = MakeKey(summary.platform, output),
                platform = summary.platform.ToString(),
                outputPath = output,
                time = DateTime.Now.ToString("o"),
                unityVersion = Application.unityVersion,
                appVersion = AppVersion(summary.platform),
                buildSeconds = (DateTime.UtcNow - summary.buildStartedAt.ToUniversalTime()).TotalSeconds,
                outputSize = size,
            };

            CollectAssets(report, snapshot);
            if (settings.analyzePackage)
                CollectPackage(output, snapshot);

            BuildSnapshot previous = BuildReportStorage.LoadLast(snapshot.key);
            if (previous != null)
            {
                snapshot.previousSize = previous.outputSize;
                snapshot.previousTime = previous.time;
                snapshot.changes = Diff(previous.assets, snapshot.assets, e => e.size);
                if (snapshot.packageGroups.Count > 0 && previous.packageGroups is { Count: > 0 })
                    snapshot.packageChanges = Diff(previous.packageGroups, snapshot.packageGroups, e => e.compressed);
            }

            bool overThreshold = snapshot.HasPrevious && Math.Abs(snapshot.Delta) > settings.ThresholdBytes;
            string lastPath = BuildReportStorage.SaveLast(snapshot);
            string historyPath = overThreshold ? BuildReportStorage.SaveHistory(snapshot) : null;

            string sizeText = BuildReportStorage.FormatSize(size);
            string deltaText = snapshot.HasPrevious ? $" ({BuildReportStorage.FormatDelta(snapshot.Delta)} so với build trước)" : " (build đầu tiên, chưa có để so sánh)";
            if (overThreshold)
                Debug.LogWarning($"[Raccoon] Build size {snapshot.key}: {sizeText}{deltaText} — vượt ngưỡng {settings.thresholdMB} MB, đã lưu report: {historyPath}");
            else
                Debug.Log($"[Raccoon] Build size {snapshot.key}: {sizeText}{deltaText}");

            if ((overThreshold || settings.alwaysReport) && !Application.isBatchMode)
            {
                string open = historyPath ?? lastPath;
                EditorApplication.delayCall += () => RaccoonBuildReportWindow.Open(open);
            }

            return historyPath;
        }

        private static string MakeKey(BuildTarget platform, string output)
        {
            string ext = File.Exists(output) ? Path.GetExtension(output).TrimStart('.').ToLowerInvariant() : "folder";
            return $"{platform}_{(string.IsNullOrEmpty(ext) ? "file" : ext)}";
        }

        private static string AppVersion(BuildTarget platform)
        {
            return platform switch
            {
                BuildTarget.Android => $"{PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})",
                BuildTarget.iOS => $"{PlayerSettings.bundleVersion} ({PlayerSettings.iOS.buildNumber})",
                _ => PlayerSettings.bundleVersion,
            };
        }

        private static long GetOutputSize(string output)
        {
            if (string.IsNullOrEmpty(output))
                return 0;
            if (File.Exists(output))
                return new FileInfo(output).Length;
            if (Directory.Exists(output))
                return new DirectoryInfo(output).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            return 0;
        }

        // Sizes are what Unity serialized for each object (before build compression).
        private static void CollectAssets(BuildReport report, BuildSnapshot snapshot)
        {
            var bySource = new Dictionary<string, (long size, string type, long biggest)>();
            var byType = new Dictionary<string, long>();

            foreach (PackedAssets packed in report.packedAssets)
            {
                foreach (PackedAssetInfo info in packed.contents)
                {
                    long size = (long)info.packedSize;
                    string type = info.type != null ? info.type.Name : "Unknown";
                    string source = string.IsNullOrEmpty(info.sourceAssetPath) ? "(built-in / generated)" : info.sourceAssetPath;

                    byType[type] = byType.TryGetValue(type, out long t) ? t + size : size;

                    // An asset's type = type of its biggest object (a texture asset is Texture2D, not its sprites).
                    if (bySource.TryGetValue(source, out var entry))
                        bySource[source] = (entry.size + size, size > entry.biggest ? type : entry.type, Math.Max(size, entry.biggest));
                    else
                        bySource[source] = (size, type, size);
                }
            }

            snapshot.assets = bySource
                .Select(kv => new SizeEntry { name = kv.Key, type = kv.Value.type, size = kv.Value.size })
                .OrderByDescending(e => e.size)
                .ToList();
            snapshot.assetTypes = byType
                .Select(kv => new SizeEntry { name = kv.Key, type = kv.Key, size = kv.Value })
                .OrderByDescending(e => e.size)
                .ToList();
        }

        // APK / AAB are zip files: read the central directory to get real (compressed) size per file.
        private static void CollectPackage(string output, BuildSnapshot snapshot)
        {
            string ext = Path.GetExtension(output).ToLowerInvariant();
            if (!File.Exists(output) || (ext != ".apk" && ext != ".aab"))
                return;

            try
            {
                var groups = new Dictionary<string, SizeEntry>();
                var entries = new List<SizeEntry>();

                using (FileStream stream = File.OpenRead(output))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry zipEntry in zip.Entries)
                    {
                        if (zipEntry.FullName.EndsWith("/"))
                            continue;

                        string group = PackageGroup(zipEntry.FullName, ext == ".aab");
                        entries.Add(new SizeEntry { name = zipEntry.FullName, type = group, size = zipEntry.Length, compressed = zipEntry.CompressedLength });

                        if (!groups.TryGetValue(group, out SizeEntry g))
                            groups[group] = g = new SizeEntry { name = group, type = group };
                        g.size += zipEntry.Length;
                        g.compressed += zipEntry.CompressedLength;
                    }
                }

                snapshot.packageGroups = groups.Values.OrderByDescending(g => g.compressed).ToList();
                snapshot.packageEntries = entries.OrderByDescending(e => e.compressed).Take(MaxPackageEntries).ToList();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Raccoon] Build Report: không đọc được nội dung {output}: {e.Message}");
            }
        }

        private static string PackageGroup(string path, bool isAab)
        {
            string[] parts = path.Split('/');
            string module = "";
            int start = 0;

            // AAB: <module>/lib/..., <module>/assets/..., plus bundle-level META-INF / BUNDLE-METADATA / BundleConfig.pb
            if (isAab && parts.Length > 1 && parts[0] != "META-INF" && parts[0] != "BUNDLE-METADATA")
            {
                module = parts[0] == "base" ? "" : parts[0] + "/";
                start = 1;
            }

            string first = parts.Length > start ? parts[start] : path;
            string file = parts[parts.Length - 1];
            int depth = parts.Length - start;

            if (first == "lib" && depth > 2)
                return module + "lib/" + parts[start + 1]; // lib/arm64-v8a
            if (first == "assets" && depth > 3 && parts[start + 1] == "bin" && parts[start + 2] == "Data")
                return module + "assets/bin/Data";
            if (file.EndsWith(".dex"))
                return module + "dex (Java/Kotlin code)";
            if (depth == 1)
                return module + "(root files)";
            return module + first;
        }

        private static List<SizeChange> Diff(List<SizeEntry> oldList, List<SizeEntry> newList, Func<SizeEntry, long> size)
        {
            var old = new Dictionary<string, SizeEntry>();
            foreach (SizeEntry e in oldList ?? new List<SizeEntry>())
                old[e.name] = e;

            var changes = new List<SizeChange>();
            foreach (SizeEntry e in newList)
            {
                long oldSize = old.TryGetValue(e.name, out SizeEntry o) ? size(o) : 0;
                if (oldSize != size(e))
                    changes.Add(new SizeChange { name = e.name, type = e.type, oldSize = oldSize, newSize = size(e) });
                old.Remove(e.name);
            }
            foreach (SizeEntry removed in old.Values)
                changes.Add(new SizeChange { name = removed.name, type = removed.type, oldSize = size(removed), newSize = 0 });

            return changes.OrderByDescending(c => Math.Abs(c.Delta)).Take(MaxChanges).ToList();
        }
    }
}
