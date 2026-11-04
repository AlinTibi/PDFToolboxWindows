using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Almarfeld.PdfEngine;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PdfEngine.Tests;

public sealed class SpreadsheetTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PDFToolboxTableTests", Guid.NewGuid().ToString("N"));
    private readonly PdfService engine = new();
    public SpreadsheetTests() => Directory.CreateDirectory(directory);
    private string At(string name) => Path.Combine(directory, name);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static readonly XNamespace SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static ExtractedTable Table(params string[][] rows) => new([1], rows, [], Enumerable.Range(0, rows[0].Length).Select(i => (double)i * 100).ToArray());
    private static XDocument Entry(ZipArchive zip, string name)
    { using var stream = zip.GetEntry(name)!.Open(); return XDocument.Load(stream); }
    private string Fixture(int tablePages = 1, bool blankPage = true, bool encrypted = false, bool signed = false, bool forms = false)
    {
        var path = At("synthetic-tables.pdf"); using var pdf = new PdfDocument();
        for (var n = 0; n < tablePages; n++)
        {
            var page = pdf.AddPage(); using var g = XGraphics.FromPdfPage(page); var font = new XFont("Segoe UI", 10);
            string[][] rows = [["ID", "Name", "Amount"], ["00123", "Example", "-12.50"], ["12345678901234567890", "România", "=1+1"]];
            for (var r = 0; r < rows.Length; r++) for (var c = 0; c < 3; c++)
                g.DrawString(rows[r][c], font, XBrushes.Black, new[] { 40, 250, 420 }[c], 80 + r * 22);
        }
        if (blankPage) { var page = pdf.AddPage(); using var g = XGraphics.FromPdfPage(page); g.DrawRectangle(XBrushes.MintCream, 20, 20, 100, 100); }
        if (encrypted) { pdf.SecuritySettings.UserPassword = "synthetic-user"; pdf.SecuritySettings.OwnerPassword = "synthetic-owner"; }
        if (signed) { var sig = new PdfDictionary(pdf); sig.Elements.SetName("/Type", "/Sig"); pdf.Internals.AddObject(sig); pdf.Internals.Catalog.Elements["/SyntheticSignature"] = sig.Reference!; }
        if (forms) { var form = new PdfDictionary(pdf); form.Elements["/Fields"] = new PdfArray(pdf); pdf.Internals.AddObject(form); pdf.Internals.Catalog.Elements["/AcroForm"] = form.Reference!; }
        pdf.Save(path); return path;
    }

    [Fact] public async Task RealPdfTextIsDetectedAndScannedPageIsReported()
    {
        var path = Fixture(); var hash = Hash(path);
        var scan = await engine.ScanTablesAsync(new(path), [1, 2], false);
        var table = Assert.Single(scan.Tables);
        Assert.Equal(3, table.ColumnCount); Assert.Equal(3, table.Rows.Length);
        Assert.Equal("00123", table.Rows[1][0]); Assert.Equal("12345678901234567890", table.Rows[2][0]);
        Assert.Equal("România", table.Rows[2][1]); Assert.Equal("=1+1", table.Rows[2][2]);
        Assert.Equal(new[] { 2 }, scan.PagesWithoutText); Assert.Equal(hash, Hash(path));
    }
    [Fact] public async Task PageRangeControlsScanAndJoinIsOptInWithoutHeaderLoss()
    {
        var path = Fixture(2, false);
        Assert.Single((await engine.ScanTablesAsync(new(path), [2], false)).Tables);
        Assert.Equal(2, (await engine.ScanTablesAsync(new(path), [1, 2], false)).Tables.Count);
        var table = Assert.Single((await engine.ScanTablesAsync(new(path), [1, 2], true)).Tables);
        Assert.Equal(6, table.Rows.Length); Assert.Equal("ID", table.Rows[3][0]); Assert.True(table.NeedsReview);
    }
    [Fact] public async Task NoTextMeansNoTablesAndNoSuccessfulExport()
    {
        var path = Fixture(); var scan = await engine.ScanTablesAsync(new(path), [2], false);
        Assert.Empty(scan.Tables); Assert.Equal(new[] { 2 }, scan.PagesWithoutText);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.SaveTablesAsync(scan, true, directory, false, [path]));
    }
    [Theory] [InlineData(true, false, false)] [InlineData(false, true, false)] [InlineData(false, false, true)]
    public async Task ExistingPdfRestrictionsArePreserved(bool encrypted, bool signed, bool forms)
    {
        var path = Fixture(encrypted: encrypted, signed: signed, forms: forms);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ScanTablesAsync(new(path, encrypted ? "synthetic-owner" : null), [1], false));
    }
    [Fact] public void XlsxUsesValidNamespacesAndTextCellsWithoutFormulaOrNumericCoercion()
    {
        using var output = new MemoryStream();
        var values = new[] { "00123", "12345678901234567890", "=1+1", "+SUM(A1)", "-12.50", "@name", "Șț 😀", "first\nsecond", " <&> " };
        SpreadsheetExport.Xlsx(output, [Table(values), Table(["Second"])], default);
        output.Position = 0; using var zip = new ZipArchive(output, ZipArchiveMode.Read);
        Assert.Equal("http://schemas.openxmlformats.org/package/2006/content-types", Entry(zip, "[Content_Types].xml").Root!.Elements().First().Name.NamespaceName);
        Assert.Equal("http://schemas.openxmlformats.org/package/2006/relationships", Entry(zip, "_rels/.rels").Root!.Elements().Single().Name.NamespaceName);
        Assert.Equal(2, Entry(zip, "xl/workbook.xml").Descendants(SheetNs + "sheet").Count());
        var sheet = Entry(zip, "xl/worksheets/sheet1.xml");
        Assert.Equal(values, sheet.Descendants(SheetNs + "t").Select(t => t.Value));
        Assert.All(sheet.Descendants(SheetNs + "c"), c => Assert.Equal("inlineStr", (string?)c.Attribute("t")));
        Assert.Empty(sheet.Descendants(SheetNs + "f")); Assert.Empty(sheet.Descendants(SheetNs + "v"));
    }
    [Fact] public void CsvQuotesCommaQuotesNewlinesAndFormulaProtectionIsOptional()
    {
        var row = new[] { "00123", "12345678901234567890", "a,b", "say \"hi\"", "line\nnext", "=1+1", "+1+1", "-12.50", "  @value" };
        using var safe = new MemoryStream(); SpreadsheetExport.Csv(safe, Table(row), true, default);
        Assert.True(safe.ToArray().Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
        var text = Encoding.UTF8.GetString(safe.ToArray());
        Assert.Contains("\"00123\",\"12345678901234567890\",\"a,b\",\"say \"\"hi\"\"\",\"line\nnext\"", text);
        Assert.Contains("\"'=1+1\",\"'+1+1\",\"'-12.50\",\"'  @value\"\r\n", text);
        using var exact = new MemoryStream(); SpreadsheetExport.Csv(exact, Table(row), false, default);
        Assert.Contains("\"=1+1\",\"+1+1\",\"-12.50\",\"  @value\"", Encoding.UTF8.GetString(exact.ToArray()));
    }
    [Fact] public async Task ExportReopensAndRenamesExistingOutputsWithoutTouchingSource()
    {
        var source = Fixture(); var hash = Hash(source); var scan = await engine.ScanTablesAsync(new(source), [1], false);
        var a = Assert.Single(await engine.SaveTablesAsync(scan, true, directory, true, [source]));
        Assert.Equal("Success", a.Status); SpreadsheetExport.ValidateXlsx(a.Path); var exportedHash = Hash(a.Path);
        var b = Assert.Single(await engine.SaveTablesAsync(scan, true, directory, true, [source]));
        Assert.Equal("Success", b.Status); Assert.NotEqual(a.Path, b.Path); Assert.Equal(exportedHash, Hash(a.Path));
        Assert.Equal(hash, Hash(source)); Assert.Empty(Directory.GetFiles(directory, ".pdftoolbox-*.tmp"));
    }
    [Fact] public async Task InvalidXmlAndOversizedCellLeaveNoOutputOrStage()
    {
        var source = Fixture();
        foreach (var value in new[] { "bad\u0001text", new string('a', 32768) })
        {
            var scan = new TableScan(source, [Table([value])], []);
            var result = Assert.Single(await engine.SaveTablesAsync(scan, true, directory, false, [source]));
            Assert.Equal("Failed", result.Status);
        }
        Assert.Empty(Directory.GetFiles(directory, "*.xlsx")); Assert.Empty(Directory.GetFiles(directory, ".pdftoolbox-*.tmp"));
    }
    private sealed class Immediate<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    [Fact] public async Task CancellationKeepsCompletedCsvAndReportsIt()
    {
        var source = Fixture(); var scan = new TableScan(source, [Table(["a", "b"]), Table(["c", "d"])], []);
        using var cancellation = new CancellationTokenSource(); var done = new List<OutputResult>();
        var completed = new Immediate<OutputResult>(r => { done.Add(r); cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.SaveTablesAsync(scan, false, directory, false, [source], cancellation: cancellation.Token, completed: completed));
        Assert.Single(done); Assert.Equal("Success", done[0].Status); Assert.True(File.Exists(done[0].Path));
        Assert.Single(Directory.GetFiles(directory, "*.csv")); Assert.Empty(Directory.GetFiles(directory, ".pdftoolbox-*.tmp"));
    }
    [Fact] public async Task BadRangesAndPrecancelledScanAreRejected()
    {
        var path = Fixture();
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ScanTablesAsync(new(path), [0], false));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ScanTablesAsync(new(path), [1, 1], false));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ScanTablesAsync(new(path), [1], false, cancellation: new CancellationToken(true)));
    }
    public void Dispose() => Directory.Delete(directory, true);
}
