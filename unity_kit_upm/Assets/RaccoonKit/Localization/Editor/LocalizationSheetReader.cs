// Reads a localization table as rows of cells from either:
//   • an .xlsx workbook (parsed directly — xlsx is a zip of XML, no extra libraries)
//   • CSV text (e.g. a Google Sheet exported with format=csv)

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Raccoon.Localization
{
    internal static class LocalizationSheetReader
    {
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace DocRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        // ─── XLSX ─────────────────────────────────────────────────────────────────

        public static List<string> GetXlsxSheetNames(string path)
        {
            using var zip = OpenXlsx(path);
            return ReadSheetEntries(zip).Select(s => s.name).ToList();
        }

        public static List<List<string>> ReadXlsx(string path, string sheetName)
        {
            using var zip = OpenXlsx(path);

            var sheets = ReadSheetEntries(zip);
            if (sheets.Count == 0)
                throw new InvalidDataException("Workbook has no sheets.");

            var sheet = sheets.FirstOrDefault(s => s.name == sheetName);
            if (sheet.entry == null)
                throw new InvalidDataException($"Sheet '{sheetName}' not found. Available: {string.Join(", ", sheets.Select(s => s.name))}");

            var sharedStrings = ReadSharedStrings(zip);
            var doc = LoadXml(zip, sheet.entry);

            var rows = new List<List<string>>();
            foreach (var rowEl in doc.Descendants(Main + "row"))
            {
                var row = new List<string>();
                foreach (var cellEl in rowEl.Elements(Main + "c"))
                {
                    string reference = (string)cellEl.Attribute("r");
                    int col = reference != null ? ColumnIndex(reference) : row.Count;
                    while (row.Count < col) row.Add("");
                    row.Add(ReadCellValue(cellEl, sharedStrings));
                }

                // Keep row positions stable even when the sheet skips empty rows
                string rowRef = (string)rowEl.Attribute("r");
                if (rowRef != null && int.TryParse(rowRef, out int rowNumber))
                    while (rows.Count < rowNumber - 1) rows.Add(new List<string>());

                rows.Add(row);
            }

            return rows;
        }

        private static ZipArchive OpenXlsx(string path)
        {
            // FileShare.ReadWrite lets us read while the workbook is still open in Excel
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new ZipArchive(stream, ZipArchiveMode.Read);
        }

        private static List<(string name, string entry)> ReadSheetEntries(ZipArchive zip)
        {
            var workbook = LoadXml(zip, "xl/workbook.xml");
            var rels = LoadXml(zip, "xl/_rels/workbook.xml.rels")
                .Descendants(PkgRel + "Relationship")
                .ToDictionary(r => (string)r.Attribute("Id"), r => (string)r.Attribute("Target"));

            var result = new List<(string, string)>();
            foreach (var sheet in workbook.Descendants(Main + "sheet"))
            {
                string id = (string)sheet.Attribute(DocRel + "id");
                if (id == null || !rels.TryGetValue(id, out string target)) continue;

                // Targets are relative to xl/ unless absolute ("/xl/worksheets/...")
                string entry = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
                result.Add(((string)sheet.Attribute("name"), entry));
            }

            return result;
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var result = new List<string>();
            if (zip.GetEntry("xl/sharedStrings.xml") == null) return result;

            foreach (var si in LoadXml(zip, "xl/sharedStrings.xml").Root.Elements(Main + "si"))
                result.Add(ReadText(si));
            return result;
        }

        private static string ReadCellValue(XElement cell, List<string> sharedStrings)
        {
            string type = (string)cell.Attribute("t");
            string raw = (string)cell.Element(Main + "v");

            switch (type)
            {
                case "s":
                    return int.TryParse(raw, out int index) && index < sharedStrings.Count ? sharedStrings[index] : "";
                case "inlineStr":
                    var inline = cell.Element(Main + "is");
                    return inline != null ? ReadText(inline) : "";
                case "b":
                    return raw == "1" ? "TRUE" : "FALSE";
                default:
                    return raw ?? "";
            }
        }

        /// <summary>
        /// Concatenates all text runs, skipping phonetic hints (rPh) that Excel
        /// stores alongside Japanese text.
        /// </summary>
        private static string ReadText(XElement container) =>
            string.Concat(container.Descendants(Main + "t")
                .Where(t => t.Parent?.Name != Main + "rPh")
                .Select(t => t.Value));

        /// <summary>"C12" → 2 (zero-based).</summary>
        private static int ColumnIndex(string cellReference)
        {
            int col = 0;
            foreach (char ch in cellReference)
            {
                if (!char.IsLetter(ch)) break;
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }

            return col - 1;
        }

        private static XDocument LoadXml(ZipArchive zip, string entryName)
        {
            var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"Missing '{entryName}' in workbook.");
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }

        // ─── CSV ──────────────────────────────────────────────────────────────────

        /// <summary>RFC 4180 parser: quoted fields, escaped quotes and embedded newlines.</summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                    }

                    continue;
                }

                switch (ch)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(field.ToString());
                        field.Clear();
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    default:
                        field.Append(ch);
                        break;
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }
    }
}
