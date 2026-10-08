using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace HealthixSFTP
{
    /// <summary>
    /// Reads the Sipuk consent report (xlsx) into ClientId -> ConsentDate (Epic 82734).
    /// Sheet 2 supplies EnterpriseClientId + SipukPatientId; sheet 1 supplies "Health Home Consent Date" by "Patient ID".
    /// Rows without a numeric EnterpriseClientId are not positively matched to Enterprise and are skipped (ADR-1).
    /// </summary>
    public static class ConsentReport
    {
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static Dictionary<int, DateTime> Load(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                List<string> sharedStrings = ReadSharedStrings(zip);
                List<string> sheetPaths = GetSheetPaths(zip);
                if (sheetPaths.Count < 2)
                    throw new InvalidDataException("Consent report must contain at least two sheets.");

                var consentDateByPatientId = new Dictionary<string, DateTime>();
                foreach (var row in ReadSheet(zip, sheetPaths[0], sharedStrings))
                {
                    string patientId = Get(row, "Patient ID");
                    DateTime? consentDate = ParseDate(Get(row, "Health Home Consent Date"));
                    if (patientId.Length > 0 && consentDate.HasValue)
                        consentDateByPatientId[patientId] = consentDate.Value;
                }

                var consents = new Dictionary<int, DateTime>();
                foreach (var row in ReadSheet(zip, sheetPaths[1], sharedStrings))
                {
                    if (!int.TryParse(Get(row, "EnterpriseClientId"), out int clientId))
                        continue;

                    string patientId = Get(row, "SipukPatientId");
                    if (!consentDateByPatientId.TryGetValue(patientId, out DateTime consentDate))
                        throw new InvalidDataException($"No Health Home Consent Date on sheet 1 for EnterpriseClientId {clientId}.");

                    if (consents.TryGetValue(clientId, out DateTime existing) && existing != consentDate)
                        throw new InvalidDataException($"EnterpriseClientId {clientId} appears more than once with different consent dates.");

                    consents[clientId] = consentDate;
                }
                return consents;
            }
        }

        private static string Get(Dictionary<string, string> row, string header)
        {
            return row.TryGetValue(header, out string value) && value != null ? value.Trim() : "";
        }

        // Excel stores dates as OADate serial numbers; fall back to text parsing for text-formatted cells.
        private static DateTime? ParseDate(string value)
        {
            if (value.Length == 0)
                return null;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial))
                return DateTime.FromOADate(serial).Date;
            if (DateTime.TryParse(value, out DateTime dt))
                return dt.Date;
            return null;
        }

        private static XDocument LoadXml(ZipArchive zip, string entryName)
        {
            ZipArchiveEntry entry = zip.GetEntry(entryName);
            if (entry == null)
                return null;
            using (Stream s = entry.Open())
                return XDocument.Load(s);
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            XDocument doc = LoadXml(zip, "xl/sharedStrings.xml");
            if (doc == null)
                return new List<string>();
            return doc.Root.Elements(Main + "si")
                .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))
                .ToList();
        }

        // Sheet file paths in workbook (tab) order.
        private static List<string> GetSheetPaths(ZipArchive zip)
        {
            XDocument workbook = LoadXml(zip, "xl/workbook.xml");
            XDocument rels = LoadXml(zip, "xl/_rels/workbook.xml.rels");
            var targetById = rels.Root.Elements(PkgRel + "Relationship")
                .ToDictionary(r => (string)r.Attribute("Id"), r => (string)r.Attribute("Target"));

            return workbook.Root.Element(Main + "sheets").Elements(Main + "sheet")
                .Select(sheet =>
                {
                    string target = targetById[(string)sheet.Attribute(Rel + "id")].TrimStart('/');
                    return target.StartsWith("xl/") ? target : "xl/" + target;
                })
                .ToList();
        }

        // Rows keyed by the header text in row 1.
        private static List<Dictionary<string, string>> ReadSheet(ZipArchive zip, string sheetPath, List<string> sharedStrings)
        {
            XDocument sheet = LoadXml(zip, sheetPath);
            var rows = sheet.Root.Element(Main + "sheetData").Elements(Main + "row")
                .Select(r => r.Elements(Main + "c").ToDictionary(
                    c => new string(((string)c.Attribute("r")).TakeWhile(char.IsLetter).ToArray()),
                    c => CellValue(c, sharedStrings)))
                .ToList();

            var result = new List<Dictionary<string, string>>();
            if (rows.Count == 0)
                return result;

            Dictionary<string, string> header = rows[0];
            foreach (var row in rows.Skip(1))
            {
                var byHeader = new Dictionary<string, string>();
                foreach (var cell in row)
                {
                    if (header.TryGetValue(cell.Key, out string name) && !string.IsNullOrWhiteSpace(name))
                        byHeader[name.Trim()] = cell.Value;
                }
                result.Add(byHeader);
            }
            return result;
        }

        private static string CellValue(XElement cell, List<string> sharedStrings)
        {
            string type = (string)cell.Attribute("t");
            if (type == "inlineStr")
                return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));

            string raw = (string)cell.Element(Main + "v");
            if (type == "s" && int.TryParse(raw, out int index))
                return sharedStrings[index];
            return raw;
        }
    }
}
