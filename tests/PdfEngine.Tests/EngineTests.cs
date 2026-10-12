using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Almarfeld.PdfEngine;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfEngine.Tests;

public sealed class EngineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PDFToolboxTests", Guid.NewGuid().ToString("N"));
    private readonly PdfService engine = new();
    public EngineTests() => Directory.CreateDirectory(directory);
    private string PathFor(string name) => Path.Combine(directory, name);
    private string Fixture(string name = "source.pdf", int pages = 4, bool encrypted = false, bool signed = false, bool forms = false)
    {
        var path = PathFor(name); using var doc = new PdfDocument();
        for (var i = 1; i <= pages; i++)
        {
            var page = doc.AddPage(); page.Width = XUnit.FromPoint(300 + i); page.Height = XUnit.FromPoint(500);
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(i % 2 == 0 ? XBrushes.ForestGreen : XBrushes.IndianRed, 10, 10, 100, 100);
            graphics.DrawString($"Synthetic page {i}", new XFont("Segoe UI", 18), XBrushes.Black, 20, 160);
        }
        doc.Info.Title = "Synthetic PDF"; doc.Info.Author = "Synthetic fixture";
        if (encrypted) { doc.SecuritySettings.UserPassword = "synthetic-user"; doc.SecuritySettings.OwnerPassword = "synthetic-owner"; }
        if (signed) { var sig = new PdfDictionary(doc); sig.Elements.SetName("/Type", "/Sig"); doc.Internals.AddObject(sig); doc.Internals.Catalog.Elements["/SyntheticSignature"] = sig.Reference!; }
        if (forms) { var form = new PdfDictionary(doc); form.Elements["/Fields"] = new PdfArray(doc); doc.Internals.AddObject(form); doc.Internals.Catalog.Elements["/AcroForm"] = form.Reference!; }
        doc.Save(path); return path;
    }
    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static double[] Widths(string path) { using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import); return doc.Pages.Cast<PdfPage>().Select(p => p.Width.Point).ToArray(); }
    private static int[] Rotations(string path) { using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import); return doc.Pages.Cast<PdfPage>().Select(p => p.Rotate).ToArray(); }
    private static void ContentsPresent(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        foreach (var page in document.Pages.Cast<PdfPage>()) Assert.True(page.Contents.Elements.Count > 0);
    }

    [Theory]
    [InlineData("1-3,5", 5, new[] { 1, 2, 3, 5 })]
    [InlineData("3,1", 3, new[] { 3, 1 })]
    [InlineData(" 1 - 2 , 4 ", 4, new[] { 1, 2, 4 })]
    public void ParsesRanges(string text, int count, int[] expected) => Assert.Equal(expected, PageRanges.Parse(text, count));
    [Theory]
    [InlineData("")][InlineData("0")][InlineData("5")][InlineData("3-1")][InlineData("1,1")][InlineData("1-3,2")][InlineData("1,")][InlineData("-1")][InlineData("1--2")][InlineData("2147483648")][InlineData("1;2")]
    public void RejectsBadRanges(string range) => Assert.Throws<PdfOperationException>(() => PageRanges.Parse(range, 4));

    [Fact] public async Task MergeReopensAndPreservesContentOrderAndSources()
    {
        var a = Fixture("a.pdf", 2); var b = Fixture("b.pdf", 3); var before = new[] { Digest(a), Digest(b) };
        var output = await engine.ExportPagesAsync([new(a), new(b)], [new(1, 3), new(1, 1), new(0, 2), new(0, 1)], PathFor("merge.pdf"), CollisionPolicy.Cancel);
        Assert.Equal(new[] { 303d, 301d, 302d, 301d }, Widths(output.Path)); ContentsPresent(output.Path);
        Assert.Equal(before, new[] { Digest(a), Digest(b) });
    }
    [Theory] [InlineData(SplitMode.EveryPage, "", 1, 4)][InlineData(SplitMode.EveryN, "", 3, 2)][InlineData(SplitMode.Ranges, "1-2;3-4", 1, 2)][InlineData(SplitMode.SelectedPages, "4,2", 1, 2)]
    public async Task SplitProducesValidParts(SplitMode mode, string range, int n, int expected)
    {
        var input = Fixture(); var hash = Digest(input);
        var results = await engine.SplitAsync(new(input), mode, range, n, directory, "split", CollisionPolicy.Cancel);
        Assert.Equal(expected, results.Count); Assert.All(results, r => { Assert.Equal("Success", r.Status); ContentsPresent(r.Path); });
        Assert.Equal(hash, Digest(input));
    }
    [Fact] public async Task InvalidSplitIsValidatedBeforeAnyOutput()
    {
        var input = Fixture(); var count = Directory.GetFiles(directory).Length;
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.SplitAsync(new(input), SplitMode.Ranges, "1;5", 1, directory, "split", CollisionPolicy.Cancel));
        Assert.Equal(count, Directory.GetFiles(directory).Length);
    }
    [Fact] public async Task ReorderDeleteDuplicateAndExtractAreRealPageSelections()
    {
        var input = Fixture();
        var result = await engine.ExportPagesAsync([new(input)], [new(0, 4), new(0, 2), new(0, 2)], PathFor("organized.pdf"), CollisionPolicy.Cancel);
        Assert.Equal(new[] { 304d, 302d, 302d }, Widths(result.Path)); ContentsPresent(result.Path);
        var extract = await engine.ExportPagesAsync([new(input)], PageRanges.Parse("1,3", 4).Select(p => new PageSelection(0, p)).ToArray(), PathFor("extract.pdf"), CollisionPolicy.Cancel);
        Assert.Equal(new[] { 301d, 303d }, Widths(extract.Path));
    }
    [Theory] [InlineData(90)][InlineData(180)][InlineData(270)][InlineData(-90)]
    public async Task RotationReopens(int rotation)
    {
        var input = Fixture(); var result = await engine.ExportPagesAsync([new(input)], [new(0, 1, rotation), new(0, 2)], PathFor("rotated.pdf"), CollisionPolicy.Cancel);
        Assert.Equal(new[] { (rotation % 360 + 360) % 360, 0 }, Rotations(result.Path)); Assert.Equal(new[] { 0, 0, 0, 0 }, Rotations(input));
    }
    [Fact] public async Task ExistingRotationIsAddedNotLost()
    {
        var input = Fixture(); using (var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify)) { document.Pages[0].Rotate = 270; document.Save(input); }
        var result = await engine.ExportPagesAsync([new(input)], [new(0, 1, 180)], PathFor("rotate.pdf"), CollisionPolicy.Cancel);
        Assert.Equal(new[] { 90 }, Rotations(result.Path));
    }
    [Fact] public async Task EmptyExportDoesNotCreateFile() => await Assert.ThrowsAsync<PdfOperationException>(() => engine.ExportPagesAsync([], [], PathFor("empty.pdf"), CollisionPolicy.Cancel));
    [Fact] public async Task InvalidInputIsRejected()
    {
        var path = PathFor("invalid.pdf"); await File.WriteAllTextAsync(path, "not a PDF");
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.InspectAsync(new(path)));
        await File.WriteAllTextAsync(path, "%PDF-1.7\nmalformed"); await Assert.ThrowsAnyAsync<Exception>(() => engine.InspectAsync(new(path)));
    }
    [Fact] public async Task EmptyInputIsRejected()
    {
        var path = PathFor("empty.pdf"); await File.WriteAllBytesAsync(path, []); await Assert.ThrowsAsync<PdfOperationException>(() => engine.InspectAsync(new(path)));
    }
    [Fact] public async Task MetadataReopensWithActualValuesWithoutChangingSource()
    {
        var input = Fixture(); var before = Digest(input);
        var result = await engine.MetadataAsync(new(input), new("Reviewed title", "", "Local subject", "test", "ALMARFELD", ""), PathFor("meta.pdf"), CollisionPolicy.Cancel);
        var info = await engine.InspectAsync(new(result.Path)); Assert.Equal("Reviewed title", info.Metadata.Title); Assert.Empty(info.Metadata.Author); Assert.Equal("Local subject", info.Metadata.Subject);
        Assert.Equal(4, info.PageCount); Assert.Equal(before, Digest(input)); ContentsPresent(result.Path);
    }
    [Fact] public async Task EncryptedInputRequiresValidPasswordAndNeverSilentlyLosesEncryption()
    {
        var input = Fixture(encrypted: true); var hash = Digest(input);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.InspectAsync(new(input)));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.InspectAsync(new(input, "wrong")));
        var info = await engine.InspectAsync(new(input, "synthetic-owner")); Assert.True(info.Encrypted); Assert.Equal(4, info.PageCount);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ExportPagesAsync([new(input, "synthetic-owner")], [new(0, 1)], PathFor("decrypted.pdf"), CollisionPolicy.Cancel));
        Assert.False(File.Exists(PathFor("decrypted.pdf"))); Assert.Equal(hash, Digest(input));
    }
    [Fact] public async Task SignedDocumentIsNotRewritten()
    {
        var input = Fixture(signed: true); Assert.True((await engine.InspectAsync(new(input))).Signed);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ExportPagesAsync([new(input)], [new(0, 1)], PathFor("signed-out.pdf"), CollisionPolicy.Cancel));
    }
    [Fact] public async Task OwnerOnlyEncryptionIsStillRecognizedAndNotRemoved()
    {
        var input = Fixture();
        using (var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify))
        { document.SecuritySettings.OwnerPassword = "synthetic-owner"; document.Save(input); }
        var hash = Digest(input);
        Assert.True((await engine.InspectAsync(new(input))).Encrypted);
        var error = await Assert.ThrowsAsync<PdfOperationException>(() => engine.ExportPagesAsync([new(input)], [new(0, 1)], PathFor("owner-out.pdf"), CollisionPolicy.Cancel));
        Assert.Equal("Encrypted", error.Code); Assert.False(File.Exists(PathFor("owner-out.pdf"))); Assert.Equal(hash, Digest(input));
    }
    [Fact] public async Task FormsAreConservativelyRefused()
    {
        var input = Fixture(forms: true); Assert.True((await engine.InspectAsync(new(input))).Forms);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.MetadataAsync(new(input), new(), PathFor("forms-out.pdf"), CollisionPolicy.Cancel));
    }
    [Fact] public async Task SourceFilenameIsProtectedEvenWithReplace()
    {
        var input = Fixture(); var hash = Digest(input);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ExportPagesAsync([new(input)], [new(0, 1)], input, CollisionPolicy.Replace)); Assert.Equal(hash, Digest(input));
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string alias, string original, IntPtr reserved);
    [Fact] public async Task SourceHardLinkAliasIsProtected()
    {
        var input = Fixture(); var alias = PathFor("alias.pdf"); var hash = Digest(input);
        Assert.True(CreateHardLink(alias, input, IntPtr.Zero));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ExportPagesAsync([new(input)], [new(0, 1)], alias, CollisionPolicy.Replace));
        Assert.Equal(hash, Digest(input)); Assert.Equal(hash, Digest(alias));
    }
    [Fact] public async Task ZeroPagePdfIsRejected()
    {
        var text = "%PDF-1.4\n";
        var one = text.Length; text += "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
        var two = text.Length; text += "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n";
        var xref = text.Length;
        text += $"xref\n0 3\n0000000000 65535 f \n{one:0000000000} 00000 n \n{two:0000000000} 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n";
        var path = PathFor("zero-pages.pdf"); File.WriteAllText(path, text, Encoding.ASCII);
        var error = await Assert.ThrowsAsync<PdfOperationException>(() => engine.InspectAsync(new(path)));
        Assert.Equal("Limit", error.Code);
    }
    [Fact] public async Task CollisionCancelDoesNotOverwriteAndRenameWorks()
    {
        var input = Fixture(); var target = Fixture("output.pdf", 2); var hash = Digest(target);
        await Assert.ThrowsAnyAsync<IOException>(() => engine.ExportPagesAsync([new(input)], [new(0, 1)], target, CollisionPolicy.Cancel)); Assert.Equal(hash, Digest(target));
        var result = await engine.ExportPagesAsync([new(input)], [new(0, 1)], target, CollisionPolicy.Rename);
        Assert.EndsWith("output (2).pdf", result.Path); Assert.Single(Widths(result.Path)); Assert.Equal(hash, Digest(target));
    }
    [Fact] public async Task ExplicitOutputReplacementIsValid()
    {
        var input = Fixture(); var target = Fixture("output.pdf", 2);
        var result = await engine.ExportPagesAsync([new(input)], [new(0, 3)], target, CollisionPolicy.Replace); Assert.Equal(new[] { 303d }, Widths(result.Path));
    }
    [Fact] public async Task LockedOutputFailsWithoutPartialOrSourceChanges()
    {
        var input = Fixture(); var hash = Digest(input); var target = Fixture("locked.pdf", 2);
        using var locked = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAnyAsync<IOException>(() => engine.ExportPagesAsync([new(input)], [new(0, 1)], target, CollisionPolicy.Replace));
        Assert.Equal(hash, Digest(input)); Assert.Empty(Directory.GetFiles(directory, ".pdftoolbox-*"));
    }
    [Fact] public async Task CancellationBeforeStartCreatesNothing()
    {
        var input = Fixture(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ExportPagesAsync([new(input)], [new(0, 1)], PathFor("cancel.pdf"), CollisionPolicy.Cancel, cancellation: cts.Token)); Assert.False(File.Exists(PathFor("cancel.pdf")));
    }
    private sealed class DirectProgress(Action<JobProgress> callback) : IProgress<JobProgress> { public void Report(JobProgress value) => callback(value); }
    [Fact] public async Task CancellationDuringWorkDiscardsStagedOutput()
    {
        var input = Fixture(); using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ExportPagesAsync([new(input)], [new(0, 1), new(0, 2)], PathFor("cancel.pdf"), CollisionPolicy.Cancel, new DirectProgress(_ => cts.Cancel()), cts.Token));
        Assert.Empty(Directory.GetFiles(directory, ".pdftoolbox-*")); Assert.False(File.Exists(PathFor("cancel.pdf")));
    }
    [Fact] public async Task PartialSplitFailureIsReportedNotHidden()
    {
        var input = Fixture(pages: 3); var existing = Fixture("parts-part-002.pdf", 1); var hash = Digest(existing);
        var results = await engine.SplitAsync(new(input), SplitMode.EveryPage, "", 1, directory, "parts", CollisionPolicy.Cancel);
        Assert.Equal(new[] { "Success", "Failed", "Success" }, results.Select(x => x.Status)); Assert.Equal(hash, Digest(existing));
        ContentsPresent(results[0].Path); ContentsPresent(results[2].Path); Assert.Empty(Directory.GetFiles(directory, ".pdftoolbox-*"));
    }
    [Fact] public void FailedWriteRemovesOnlyItsOwnTemporary()
    {
        var input = Fixture(); var original = Digest(input);
        Assert.Throws<IOException>(() => SafeOutput.Commit(PathFor("failure.pdf"), CollisionPolicy.Cancel, [input], s => { s.WriteByte(1); throw new IOException("Synthetic failure"); }, _ => { }, default));
        Assert.False(File.Exists(PathFor("failure.pdf"))); Assert.Empty(Directory.GetFiles(directory, ".pdftoolbox-*")); Assert.Equal(original, Digest(input));
    }
    [Fact] public async Task UnicodeAndLongPathsWork()
    {
        var nested = Path.Combine(directory, string.Concat(Enumerable.Repeat("long-folder-", 8)), string.Concat(Enumerable.Repeat("another-", 10)));
        Directory.CreateDirectory(nested); var input = Fixture("Știință_日本語.pdf"); var target = Path.Combine(nested, "результат_ș.pdf"); Assert.True(target.Length > 260);
        var result = await engine.ExportPagesAsync([new(input)], [new(0, 2)], target, CollisionPolicy.Cancel); Assert.Equal(new[] { 302d }, Widths(result.Path));
    }
    private string ImageFixture(string extension)
    {
        var pixels = Enumerable.Repeat((byte)190, 30 * 20 * 3).ToArray(); var image = BitmapSource.Create(30, 20, 96, 96, PixelFormats.Rgb24, null, pixels, 90); image.Freeze();
        BitmapEncoder encoder = extension switch { ".jpg" => new JpegBitmapEncoder(), ".bmp" => new BmpBitmapEncoder(), _ => new PngBitmapEncoder() };
        encoder.Frames.Add(BitmapFrame.Create(image)); var path = PathFor("synthetic" + extension); using var stream = File.Create(path); encoder.Save(stream); return path;
    }
    [Theory] [InlineData(".png", ImageFit.Fit)][InlineData(".jpg", ImageFit.Fill)][InlineData(".bmp", ImageFit.Fit)]
    public async Task ImagesToPdfContainsImageAndReopens(string extension, ImageFit fit)
    {
        var image = ImageFixture(extension); var hash = Digest(image);
        var result = await engine.ImagesAsync([image, image], new("Letter", true, 24, fit, 85), PathFor("images.pdf"), CollisionPolicy.Cancel);
        Assert.Equal(new[] { 792d, 792d }, Widths(result.Path)); ContentsPresent(result.Path); Assert.Equal(hash, Digest(image));
        var rendered = await new ThumbnailService().GetAsync(new(result.Path), 1); Assert.True(rendered.PixelWidth > 0);
    }
    [Fact] public async Task UnsupportedImageDoesNotCreatePdf()
    {
        var image = PathFor("image.webp"); await File.WriteAllTextAsync(image, "synthetic");
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ImagesAsync([image], new(), PathFor("bad-image.pdf"), CollisionPolicy.Cancel)); Assert.False(File.Exists(PathFor("bad-image.pdf")));
    }
    [Fact] public async Task ThumbnailsAreRealRenderedPagesWithBoundedCache()
    {
        var input = Fixture(pages: 20); var service = new ThumbnailService();
        var first = await service.GetAsync(new(input), 1); var second = await service.GetAsync(new(input), 2);
        var a = new byte[first.PixelWidth * first.PixelHeight * 4]; first.CopyPixels(a, first.PixelWidth * 4, 0);
        var b = new byte[second.PixelWidth * second.PixelHeight * 4]; second.CopyPixels(b, second.PixelWidth * 4, 0); Assert.NotEqual(Convert.ToHexString(SHA256.HashData(a)), Convert.ToHexString(SHA256.HashData(b)));
        for (var p = 3; p <= 20; p++) await service.GetAsync(new(input), p);
        Assert.Equal(16, service.CachedCount); await service.ClearAsync(); Assert.Equal(0, service.CachedCount);
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
