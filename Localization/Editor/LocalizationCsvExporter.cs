// Splits a localization table into one "key,value" CSV per language,
// in the format LocalizationManager.ParseCSV reads.
//
// Expected table layout (rows above the header, e.g. a title row, are ignored):
//   Key        | Context / Description | English (en) | Vietnamese (vi) | ...
//   MENU_PLAY  | Main menu play button | Play         | Chơi            | ...
//
// • The header row is the first row with a cell equal to "Key".
// • Language columns are those whose header ends with "(code)"; others are skipped.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Raccoon.Localization
{
    internal static class LocalizationCsvExporter
    {
        private static readonly Regex LanguageCodePattern = new(@"\(([^()]+)\)\s*$");
        private static readonly UTF8Encoding Utf8NoBom = new(false);

        internal class Result
        {
            public readonly List<string> Files = new();
            public readonly List<string> Warnings = new();
        }

        /// <param name="fallbackToEnglish">
        /// Fill empty translations with the English (or first language) value.
        /// When false, empty cells are left out so LocalizationManager reports them as missing.
        /// </param>
        public static Result Export(List<List<string>> rows, string outputFolder, bool fallbackToEnglish)
        {
            var result = new Result();

            int headerRow = rows.FindIndex(r => r.Any(IsKeyHeader));
            if (headerRow < 0)
                throw new InvalidDataException("Header row not found — expected a column named \"Key\".");

            var header = rows[headerRow];
            int keyCol = header.FindIndex(IsKeyHeader);

            var languages = new List<(int col, string code)>();
            for (int c = 0; c < header.Count; c++)
            {
                if (c == keyCol) continue;
                var match = LanguageCodePattern.Match(header[c] ?? "");
                if (match.Success) languages.Add((c, match.Groups[1].Value.Trim()));
            }

            if (languages.Count == 0)
                throw new InvalidDataException("No language columns found — headers must end with a code, e.g. \"English (en)\".");

            WarnUnknownCodes(languages.Select(l => l.code), result);

            int english = languages.FindIndex(l => l.code.Equals("en", StringComparison.OrdinalIgnoreCase));
            int fallbackCol = languages[english >= 0 ? english : 0].col;

            var entries = CollectEntries(rows, headerRow + 1, keyCol, result);

            Directory.CreateDirectory(outputFolder);
            foreach (var (col, code) in languages)
            {
                var sb = new StringBuilder("key,value\n");
                int empty = 0;

                foreach (var (key, row) in entries)
                {
                    string value = Cell(row, col);
                    if (value.Length == 0 && fallbackToEnglish)
                        value = Cell(row, fallbackCol);

                    if (value.Length == 0)
                    {
                        empty++;
                        continue;
                    }

                    sb.Append(key).Append(',').Append(EscapeValue(value)).Append('\n');
                }

                if (empty > 0)
                    result.Warnings.Add($"{code}: {empty} empty translation(s) skipped.");

                WriteIfChanged(Path.Combine(outputFolder, code + ".csv"), sb.ToString());
                result.Files.Add($"{code}.csv ({entries.Count - empty} keys)");
            }

            return result;
        }

        private static List<(string key, List<string> row)> CollectEntries(
            List<List<string>> rows, int firstDataRow, int keyCol, Result result)
        {
            var entries = new List<(string, List<string>)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int r = firstDataRow; r < rows.Count; r++)
            {
                string key = Cell(rows[r], keyCol).Trim();
                if (key.Length == 0) continue;

                if (key.Contains(','))
                {
                    result.Warnings.Add($"Key '{key}' contains a comma and was skipped.");
                    continue;
                }

                if (!seen.Add(key))
                {
                    result.Warnings.Add($"Duplicate key '{key}' — only the first row is used.");
                    continue;
                }

                entries.Add((key, rows[r]));
            }

            return entries;
        }

        private static void WarnUnknownCodes(IEnumerable<string> codes, Result result)
        {
            var supported = new HashSet<string>(
                Enum.GetValues(typeof(Language)).Cast<Language>().Select(LocalizationManager.LanguageToFileName));

            foreach (string code in codes.Where(c => !supported.Contains(c)))
                result.Warnings.Add($"'{code}.csv' has no matching Language enum entry — LocalizationManager will never load it.");
        }

        /// <summary>
        /// LocalizationManager reads one line per entry and turns a literal "\n" into a newline,
        /// so real newlines are written as "\n". Quoted when needed to keep commas,
        /// quotes and leading/trailing spaces intact.
        /// </summary>
        private static string EscapeValue(string value)
        {
            value = value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\\n");
            bool needsQuotes = value.Contains(',') || value.Contains('"') || value != value.Trim();
            return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }

        private static void WriteIfChanged(string path, string content)
        {
            // Skip identical files so Unity doesn't reimport them for nothing
            if (File.Exists(path) && File.ReadAllText(path) == content) return;
            File.WriteAllText(path, content, Utf8NoBom);
        }

        private static bool IsKeyHeader(string cell) =>
            string.Equals(cell?.Trim(), "key", StringComparison.OrdinalIgnoreCase);

        private static string Cell(List<string> row, int col) =>
            col >= 0 && col < row.Count ? row[col] ?? "" : "";
    }
}
