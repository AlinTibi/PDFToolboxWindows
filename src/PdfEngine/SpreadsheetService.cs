using System.IO;
using PdfSharp.Pdf.IO;

namespace Almarfeld.PdfEngine;

public sealed partial class PdfService
{
    public Task<TableScan> ScanTablesAsync(PdfInput input, IReadOnlyList<int> pages, bool joinConsecutive,
        IProgress<JobProgress>? progress = null, CancellationToken cancellation = default) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        using var opened = new Opened(input, PdfDocumentOpenMode.Import);
        Editable(opened); // Keep existing restrictions; no silent decryption or signed/form processing.
        if (pages.Count == 0 || pages.Distinct().Count() != pages.Count || pages.Any(p => p < 1 || p > opened.Document.PageCount))
            throw new PdfOperationException("Range", "Choose valid, distinct PDF pages.");
        opened.Stream.Position = 0;
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(opened.Stream);
        var tables = new List<ExtractedTable>(); var withoutText = new List<int>();
        long glyphCount = 0, cells = 0, characters = 0; var rows = 0;
        for (var i = 0; i < pages.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var page = pdf.GetPage(pages[i]); var letters = page.Letters;
            glyphCount += letters.Count;
            if (letters.Count > 200000 || glyphCount > 2000000) throw new PdfOperationException("Limit", "Too much text to preview safely. Select a smaller page range.");
            if (letters.Count(l => !string.IsNullOrWhiteSpace(l.Value)) < 8) withoutText.Add(pages[i]);
            else
            {
                // Use text advances, not ink bounding boxes: narrow glyphs otherwise acquire
                // artificial spaces (for example 00123 -> 00 1 23).
                var glyphs = letters.Select(l => new TableGlyph(l.Value, l.StartBaseLine.X,
                    page.Height - l.StartBaseLine.Y, Math.Max(l.Width, l.EndBaseLine.X - l.StartBaseLine.X), Math.Max(1, l.PointSize))).ToArray();
                foreach (var table in TableDetection.Detect(pages[i], glyphs))
                {
                    tables.Add(table); rows += table.Rows.Length; cells += (long)table.Rows.Length * table.ColumnCount;
                    characters += table.Rows.Sum(r => r.Sum(v => (long)v.Length));
                    if (tables.Count > 1000 || rows > 100000 || cells > 1000000 || characters > 10000000)
                        throw new PdfOperationException("Limit", "The table preview limit was reached. Scan a smaller page range; no partial scan will be exported.");
                }
            }
            progress?.Report(new(i + 1, pages.Count, "Finding tables"));
        }
        cancellation.ThrowIfCancellationRequested();
        return new TableScan(input.Path, joinConsecutive ? TableDetection.JoinConsecutive(tables) : tables, withoutText.ToArray());
    }, cancellation);

    public Task<IReadOnlyList<OutputResult>> SaveTablesAsync(TableScan scan, bool xlsx, string folder, bool protectCsv,
        IEnumerable<string> sources, IProgress<JobProgress>? progress = null, CancellationToken cancellation = default,
        IProgress<OutputResult>? completed = null) => Task.Run<IReadOnlyList<OutputResult>>(() =>
    {
        if (scan.Tables.Count == 0) throw new PdfOperationException("Empty", "Find and review tables before exporting.");
        var sourcePaths = sources.Append(scan.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var name = System.IO.Path.GetFileNameWithoutExtension(scan.SourcePath);
        var results = new List<OutputResult>(); var count = xlsx ? 1 : scan.Tables.Count;
        for (var i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var target = System.IO.Path.Combine(folder, xlsx ? name + "_tables.xlsx" : $"{name}_table_{i + 1:D3}.csv");
            try
            {
                var path = SafeOutput.Commit(target, CollisionPolicy.Rename, sourcePaths,
                    stream => { if (xlsx) SpreadsheetExport.Xlsx(stream, scan.Tables, cancellation); else SpreadsheetExport.Csv(stream, scan.Tables[i], protectCsv, cancellation); },
                    temporary =>
                    {
                        if (xlsx) SpreadsheetExport.ValidateXlsx(temporary);
                        else { using var reader = new StreamReader(temporary, new System.Text.UTF8Encoding(false, true)); while (reader.ReadLine() != null) cancellation.ThrowIfCancellationRequested(); }
                    }, cancellation);
                results.Add(new(path, "Success", xlsx ? "One sheet per table; all cells stored as text." : "UTF-8 BOM CSV; review spreadsheet import settings.", new FileInfo(path).Length));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PdfOperationException or System.Xml.XmlException)
            { results.Add(new(target, "Failed", e.Message)); }
            completed?.Report(results[^1]);
            progress?.Report(new(i + 1, count, "Exporting tables"));
        }
        return results;
    }, cancellation);
}
