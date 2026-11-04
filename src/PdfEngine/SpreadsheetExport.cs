using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Almarfeld.PdfEngine;

public static class SpreadsheetExport
{
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static void Csv(Stream output, ExtractedTable table, bool protectFormulas, CancellationToken token)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(true), 65536, leaveOpen: true) { NewLine = "\r\n" };
        foreach (var row in table.Rows)
        {
            token.ThrowIfCancellationRequested();
            writer.WriteLine(string.Join(",", row.Select(raw =>
            {
                var value = protectFormulas && FormulaRisk(raw) ? "'" + raw : raw;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            })));
        }
    }
    public static bool FormulaRisk(string value) => value.TrimStart() is { Length: > 0 } text && "=+-@".Contains(text[0]);

    public static void Xlsx(Stream output, IReadOnlyList<ExtractedTable> tables, CancellationToken token)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        void Xml(string name, Action<XmlWriter> write)
        {
            using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
            writer.WriteStartDocument(); write(writer); writer.WriteEndDocument();
        }
        Xml("[Content_Types].xml", w =>
        {
            w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
            void Type(string kind, string key, string value, string type)
            { w.WriteStartElement(kind); w.WriteAttributeString(key, value); w.WriteAttributeString("ContentType", type); w.WriteEndElement(); }
            Type("Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml");
            Type("Default", "Extension", "xml", "application/xml");
            Type("Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            for (var i = 1; i <= tables.Count; i++) Type("Override", "PartName", $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            w.WriteEndElement();
        });
        void Relationships(string path, IEnumerable<(string Id, string Type, string Target)> entries) => Xml(path, w =>
        {
            w.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
            foreach (var e in entries)
            { w.WriteStartElement("Relationship"); w.WriteAttributeString("Id", e.Id); w.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/" + e.Type); w.WriteAttributeString("Target", e.Target); w.WriteEndElement(); }
            w.WriteEndElement();
        });
        Relationships("_rels/.rels", [("rId1", "officeDocument", "xl/workbook.xml")]);
        Relationships("xl/_rels/workbook.xml.rels", Enumerable.Range(1, tables.Count).Select(i => ($"rId{i}", "worksheet", $"worksheets/sheet{i}.xml")));
        Xml("xl/workbook.xml", w =>
        {
            w.WriteStartElement("workbook", SpreadsheetNs); w.WriteStartElement("sheets", SpreadsheetNs);
            for (var i = 1; i <= tables.Count; i++)
            {
                w.WriteStartElement("sheet", SpreadsheetNs); w.WriteAttributeString("name", $"Table {i}");
                w.WriteAttributeString("sheetId", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                w.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", $"rId{i}"); w.WriteEndElement();
            }
            w.WriteEndElement(); w.WriteEndElement();
        });
        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            Xml($"xl/worksheets/sheet{i + 1}.xml", w =>
            {
                w.WriteStartElement("worksheet", SpreadsheetNs); w.WriteStartElement("sheetData", SpreadsheetNs);
                for (var r = 0; r < table.Rows.Length; r++)
                {
                    token.ThrowIfCancellationRequested(); w.WriteStartElement("row", SpreadsheetNs);
                    w.WriteAttributeString("r", (r + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    for (var c = 0; c < table.ColumnCount; c++)
                    {
                        var value = table.Rows[r][c];
                        if (value.Length > 32767) throw new PdfOperationException("Limit", "A cell exceeds Excel's 32,767-character limit. Nothing was exported.");
                        w.WriteStartElement("c", SpreadsheetNs); w.WriteAttributeString("r", Column(c) + (r + 1)); w.WriteAttributeString("t", "inlineStr");
                        w.WriteStartElement("is", SpreadsheetNs); w.WriteStartElement("t", SpreadsheetNs);
                        w.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                        w.WriteString(value); w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
                    }
                    w.WriteEndElement();
                }
                w.WriteEndElement(); w.WriteEndElement();
            });
        }
    }
    private static string Column(int index)
    {
        var result = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) result = (char)('A' + (n - 1) % 26) + result;
        return result;
    }

    public static void ValidateXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry("xl/workbook.xml") == null || zip.GetEntry("[Content_Types].xml") == null) throw new IOException("Workbook is incomplete.");
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read()) { }
        }
    }
}
