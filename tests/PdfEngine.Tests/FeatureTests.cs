using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Almarfeld.PdfEngine;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace PdfEngine.Tests;

public sealed class FeatureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PDFToolboxFeatureTests", Guid.NewGuid().ToString("N"));
    private readonly PdfService engine = new();
    private readonly ITestOutputHelper output;
    public FeatureTests(ITestOutputHelper output) { this.output = output; Directory.CreateDirectory(directory); }
    private string At(string name) => Path.Combine(directory, name);
    private string Pdf(int pages = 3, bool encrypted = false)
    {
        var path = At("source.pdf"); using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var p = doc.AddPage(); p.Width = XUnit.FromPoint(i % 3 == 1 ? 500 : 300); p.Height = XUnit.FromPoint(i % 3 == 1 ? 300 : 500);
            p.Rotate = i % 3 == 2 ? 90 : 0;
            using var g = XGraphics.FromPdfPage(p); g.DrawRectangle(XBrushes.CornflowerBlue, 8, 8, 40, 40);
        }
        if (encrypted) { doc.SecuritySettings.UserPassword = "synthetic-password"; doc.SecuritySettings.OwnerPassword = "synthetic-owner"; }
        doc.Save(path); return path;
    }
    private string Image()
    {
        var image = At("mark.png"); var pixels = Enumerable.Repeat((byte)0, 40 * 20 * 4).ToArray();
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = 255; }
        var frame = BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgra32, null, pixels, 160);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame)); using var file = File.Create(image); encoder.Save(file); return image;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static BitmapSource Decode(string path) { using var file = File.OpenRead(path); return BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0]; }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bytes, converted.PixelWidth * 4, 0); return bytes;
    }
    private static (int Left, int Top, int Right, int Bottom) Difference(BitmapSource before, BitmapSource after)
    {
        Assert.Equal(before.PixelWidth, after.PixelWidth); Assert.Equal(before.PixelHeight, after.PixelHeight);
        var a = Pixels(before); var b = Pixels(after); var left = before.PixelWidth; var top = before.PixelHeight; var right = -1; var bottom = -1;
        for (var y = 0; y < before.PixelHeight; y++) for (var x = 0; x < before.PixelWidth; x++)
        {
            var offset = (y * before.PixelWidth + x) * 4;
            if (Enumerable.Range(0, 3).Any(c => Math.Abs(a[offset + c] - b[offset + c]) > 8)) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        }
        return (left, top, right, bottom);
    }
    private async Task<BitmapSource> Render(string path, int page) => await new ThumbnailService().PreviewAsync(new(path), page, 600, "test");
    private static void Reopen(string path, int count)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import); Assert.Equal(count, doc.PageCount);
        Assert.All(doc.Pages.Cast<PdfPage>(), p => Assert.True(p.Contents.Elements.Count > 0));
    }

    [Theory][InlineData(RasterFormat.Png)][InlineData(RasterFormat.Jpeg)]
    public async Task RasterDecodesMixedSizesRotationsAndDeterministicNames(RasterFormat format)
    {
        var input = Pdf(); var hash = Hash(input); var options = new RasterOptions(format, 72, 87, "résultat");
        var results = await engine.RasterAsync(new(input), [3, 1, 2], options, directory, CollisionPolicy.Cancel);
        Assert.All(results, r => Assert.Equal("Success", r.Status));
        Assert.Equal(Enumerable.Range(1, 3).Select(p => PdfService.RasterName(options, p)), results.Select(r => Path.GetFileName(r.Path)));
        var images = results.Select(r => Decode(r.Path)).ToArray();
        Assert.Equal(new[] { 300, 500, 500 }, images.Select(i => i.PixelWidth)); Assert.Equal(new[] { 500, 300, 300 }, images.Select(i => i.PixelHeight));
        Assert.All(images, i => Assert.InRange(i.DpiX, 71.9, 72.1));
        Assert.All(images, i => Assert.Contains(Pixels(i), b => b != 255)); Assert.Equal(hash, Hash(input));
    }
    [Fact] public async Task RasterRangeOnlyAndInvalidRangeCreatesNothing()
    {
        var input = Pdf(); var selected = PageRanges.Parse("1,3", 3);
        var results = await engine.RasterAsync(new(input), selected, new(), directory, CollisionPolicy.Cancel); Assert.Equal(2, results.Count);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.RasterAsync(new(input), [4], new(Prefix: "invalid"), directory, CollisionPolicy.Cancel));
        Assert.Empty(Directory.GetFiles(directory, "invalid*"));
    }
    [Fact] public async Task RasterConflictCancelRenameAndExplicitReplace()
    {
        var input = Pdf(); var options = new RasterOptions(Dpi: 72); var original = await engine.RasterAsync(new(input), [1], options, directory, CollisionPolicy.Cancel);
        var hash = Hash(original[0].Path);
        var cancelled = await engine.RasterAsync(new(input), [1], options, directory, CollisionPolicy.Cancel); Assert.Equal("Failed", cancelled[0].Status); Assert.Equal(hash, Hash(original[0].Path));
        var renamed = await engine.RasterAsync(new(input), [1], options, directory, CollisionPolicy.Rename); Assert.Contains("(2)", renamed[0].Path);
        var notApproved = await engine.RasterAsync(new(input), [1], new(Dpi: 96), directory, CollisionPolicy.Replace); Assert.Equal("Failed", notApproved[0].Status);
        var replaced = await engine.RasterAsync(new(input), [1], new(Dpi: 96), directory, CollisionPolicy.Replace, approvedReplacements: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { original[0].Path });
        Assert.Equal("Success", replaced[0].Status); Assert.NotEqual(hash, Hash(original[0].Path)); Assert.Equal(400, Decode(original[0].Path).PixelWidth);
    }
    private sealed class CancelProgress(CancellationTokenSource source) : IProgress<JobProgress> { public void Report(JobProgress value) => source.Cancel(); }
    [Fact] public async Task RasterCancellationRetainsOnlyCompletedPagesAndCleansTemporary()
    {
        var input = Pdf(); using var cancel = new CancellationTokenSource();
        var results = await engine.RasterAsync(new(input), [1, 2, 3], new(Dpi: 72), directory, CollisionPolicy.Cancel, new CancelProgress(cancel), cancel.Token);
        Assert.Equal(new[] { "Success", "Skipped", "Skipped" }, results.Select(r => r.Status)); Assert.Single(Directory.GetFiles(directory, "*.png")); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    [Fact] public async Task RasterLockedOutputIsHonestPartialFailure()
    {
        var input = Pdf(); var locked = At("document-page-002.png"); File.WriteAllText(locked, "keep");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var results = await engine.RasterAsync(new(input), [1, 2, 3], new(Dpi: 72), directory, CollisionPolicy.Cancel);
        Assert.Equal(new[] { "Success", "Failed", "Success" }, results.Select(r => r.Status)); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    [Fact] public async Task EncryptedPdfCannotBeSilentlyRasterizedOrEdited()
    {
        var input = new PdfInput(Pdf(encrypted: true), "synthetic-password");
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.RasterAsync(input, [1], new(), directory, CollisionPolicy.Cancel));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.WatermarkAsync(input, [1], new(), At("mark.pdf"), CollisionPolicy.Cancel));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.NumbersAsync(input, [1], new(), At("num.pdf"), CollisionPolicy.Cancel));
    }
    [Theory][InlineData(OverlayPosition.TopLeft)][InlineData(OverlayPosition.TopCenter)][InlineData(OverlayPosition.TopRight)]
    [InlineData(OverlayPosition.CenterLeft)][InlineData(OverlayPosition.Center)][InlineData(OverlayPosition.CenterRight)]
    [InlineData(OverlayPosition.BottomLeft)][InlineData(OverlayPosition.BottomCenter)][InlineData(OverlayPosition.BottomRight)]
    public async Task TextWatermarkPositionsOnPortraitLandscapeAndRotatedPages(OverlayPosition position)
    {
        var input = Pdf(); var hash = Hash(input); var output = At("mark.pdf");
        await engine.WatermarkAsync(new(input), [1, 2, 3], new("TEST", FontSize: 18, Opacity: .65, Rotation: 0, Position: position), output, CollisionPolicy.Cancel);
        Reopen(output, 3);
        using var original = PdfReader.Open(input, PdfDocumentOpenMode.Import); using var modified = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        for (var page = 1; page <= 3; page++)
        {
            Assert.Equal(original.Pages[page - 1].MediaBox, modified.Pages[page - 1].MediaBox); Assert.Equal(original.Pages[page - 1].Rotate, modified.Pages[page - 1].Rotate);
            var a = await Render(input, page); var diff = Difference(a, await Render(output, page)); Assert.True(diff.Right >= diff.Left);
            var x = (diff.Left + diff.Right) / 2d / a.PixelWidth; var y = (diff.Top + diff.Bottom) / 2d / a.PixelHeight;
            if (position.ToString().EndsWith("Left")) Assert.True(x < .3, $"page {page}: x={x}");
            if (position.ToString().EndsWith("Right")) Assert.True(x > .7, $"page {page}: x={x}");
            if (position.ToString().StartsWith("Top")) Assert.True(y < .25, $"page {page}: y={y}");
            if (position.ToString().StartsWith("Bottom")) Assert.True(y > .75, $"page {page}: y={y}");
        }
        Assert.Equal(hash, Hash(input));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OpacityRotationAndSelectedPagesAreActuallyRendered(bool image)
    {
        var input = Pdf(); var hash = Hash(input); var imagePath = image ? Image() : null; var output = At("visible.pdf"); var transparent = At("transparent.pdf");
        var options = new WatermarkOptions("VISIBLE", imagePath, FontSize: 20, Rotation: 45, Opacity: .4);
        await engine.WatermarkAsync(new(input), [2], options, output, CollisionPolicy.Cancel);
        await engine.WatermarkAsync(new(input), [2], options with { Opacity = 0 }, transparent, CollisionPolicy.Cancel);
        Reopen(output, 3); Reopen(transparent, 3);
        Assert.Equal(Pixels(await Render(input, 1)), Pixels(await Render(output, 1)));
        var before = await Render(input, 2); var difference = Difference(before, await Render(output, 2));
        Assert.True(difference.Right > difference.Left); Assert.True(difference.Bottom > difference.Top);
        Assert.Equal(Pixels(before), Pixels(await Render(transparent, 2))); Assert.Equal(hash, Hash(input));
    }
    [Theory][InlineData(OverlayPosition.BottomLeft)][InlineData(OverlayPosition.BottomCenter)][InlineData(OverlayPosition.BottomRight)]
    [InlineData(OverlayPosition.TopLeft)][InlineData(OverlayPosition.TopCenter)][InlineData(OverlayPosition.TopRight)]
    public async Task NumbersRespectSelectionSkipStartAffixesAndRotations(OverlayPosition position)
    {
        var input = Pdf(); var hash = Hash(input); var options = new NumberOptions(7, "[", "]", NumberFormat.PageNumberOfTotal, Position: position, Skip: 1);
        Assert.Equal("[Page 7 of 8]", PdfService.NumberLabel(options, 0, 2)); Assert.Equal("[Page 8 of 8]", PdfService.NumberLabel(options, 1, 2));
        var output = At("numbers.pdf"); await engine.NumbersAsync(new(input), [1, 2, 3], options, output, CollisionPolicy.Cancel); Reopen(output, 3);
        Assert.Equal(Pixels(await Render(input, 1)), Pixels(await Render(output, 1)));
        foreach (var p in new[] { 2, 3 })
        {
            var before = await Render(input, p); var diff = Difference(before, await Render(output, p)); Assert.True(diff.Right > diff.Left);
            Assert.True(position.ToString().StartsWith("Top") ? diff.Bottom < before.PixelHeight * .25 : diff.Top > before.PixelHeight * .75);
        }
        Assert.Equal(hash, Hash(input));
    }
    [Theory][InlineData(NumberFormat.Number, "5")][InlineData(NumberFormat.PageNumber, "Page 5")][InlineData(NumberFormat.NumberOfTotal, "5 / 7")][InlineData(NumberFormat.PageNumberOfTotal, "Page 5 of 7")]
    public void NumberFormatsAreExplicit(NumberFormat format, string expected) => Assert.Equal(expected, PdfService.NumberLabel(new(5, Format: format), 0, 3));
    [Fact] public async Task PreviewMatchesRealExportAndCacheIsBounded()
    {
        var input = new PdfInput(Pdf()); var options = new WatermarkOptions("PREVIEW", FontSize: 18, Rotation: 0);
        var service = new ThumbnailService(); var preview = await service.PreviewAsync(input, 2, 600, "mark", ct => engine.OverlayPreviewAsync(input, 2, [2], options, null, ct));
        var output = At("export.pdf"); await engine.WatermarkAsync(input, [2], options, output, CollisionPolicy.Cancel);
        Assert.Equal(Pixels(await Render(output, 2)), Pixels(preview));
        for (var i = 0; i < 20; i++) await service.PreviewAsync(input, 1, 120, $"version{i}"); Assert.Equal(16, service.CachedCount);
        await service.ClearAsync(); Assert.Equal(0, service.CachedCount);
    }
    [Fact] public async Task InvalidOptionsAndCancellationNeverProduceOutput()
    {
        var input = new PdfInput(Pdf());
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.WatermarkAsync(input, [1], new(Opacity: double.NaN), At("bad.pdf"), CollisionPolicy.Cancel));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.NumbersAsync(input, [1], new(Position: OverlayPosition.Center), At("bad.pdf"), CollisionPolicy.Cancel));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.NumbersAsync(input, [1], new(Skip: 1), At("bad.pdf"), CollisionPolicy.Cancel));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.WatermarkAsync(input, [1], new(), At("bad.pdf"), CollisionPolicy.Cancel, cancellation: cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.RasterAsync(input, [1], new(), directory, CollisionPolicy.Cancel, cancellation: cancel.Token));
        Assert.False(File.Exists(At("bad.pdf"))); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    [Fact] public async Task LargeSyntheticDocumentExports120PagesWithoutChangingSource()
    {
        var input = Pdf(120); var hash = Hash(input); var watch = Stopwatch.StartNew(); var process = Process.GetCurrentProcess();
        var first = await new ThumbnailService().GetAsync(new(input), 1); var firstMs = watch.ElapsedMilliseconds;
        await engine.NumbersAsync(new(input), Enumerable.Range(1, 120).ToArray(), new(), At("large-numbered.pdf"), CollisionPolicy.Cancel); Reopen(At("large-numbered.pdf"), 120);
        var results = await engine.RasterAsync(new(input), Enumerable.Range(1, 120).ToArray(), new(Dpi: 36), directory, CollisionPolicy.Cancel);
        Assert.Equal(120, results.Count); Assert.All(results, r => { Assert.Equal("Success", r.Status); Assert.True(Decode(r.Path).PixelWidth > 0); });
        Assert.Equal(hash, Hash(input)); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        output.WriteLine($"120-page mixed geometry test: first preview {firstMs} ms; total {watch.ElapsedMilliseconds} ms; process peak working set {process.PeakWorkingSet64 / 1024 / 1024} MiB (includes test host).");
    }
    [Fact] public async Task NumberPreviewMatchesOutputAndUnselectedPageIsUnchanged()
    {
        var input = new PdfInput(Pdf()); var options = new NumberOptions(5, "[", "]", NumberFormat.NumberOfTotal, Skip: 1);
        var service = new ThumbnailService(); var preview = await service.PreviewAsync(input, 3, 600, "numbers", ct => engine.OverlayPreviewAsync(input, 3, [1, 3], null, options, ct));
        var path = At("numbers-preview.pdf"); await engine.NumbersAsync(input, [1, 3], options, path, CollisionPolicy.Cancel); Reopen(path, 3);
        Assert.Equal(Pixels(preview), Pixels(await Render(path, 3))); Assert.Equal(Pixels(await Render(input.Path, 2)), Pixels(await Render(path, 2)));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OverlayCancellationBetweenPagesLeavesNoFinalOrTemporary(bool numbers)
    {
        var input = new PdfInput(Pdf()); var hash = Hash(input.Path); using var cancel = new CancellationTokenSource();
        var progress = new CancelProgress(cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => numbers
            ? engine.NumbersAsync(input, [1, 2, 3], new(), At("cancel.pdf"), CollisionPolicy.Cancel, progress, cancel.Token)
            : engine.WatermarkAsync(input, [1, 2, 3], new(FontSize: 18), At("cancel.pdf"), CollisionPolicy.Cancel, progress, cancel.Token));
        Assert.False(File.Exists(At("cancel.pdf"))); Assert.Empty(Directory.GetFiles(directory, "*.tmp")); Assert.Equal(hash, Hash(input.Path));
    }
    [Fact] public async Task CropBoxAndRotationKeepVisibleNumberAtBottom()
    {
        var source = Pdf(); using (var doc = PdfReader.Open(source, PdfDocumentOpenMode.Modify))
        {
            doc.Pages[2].CropBox = new PdfRectangle(new XPoint(40, 70), new XPoint(270, 470)); doc.Save(At("cropped.pdf"));
        }
        var path = At("cropped-numbered.pdf"); await engine.NumbersAsync(new(At("cropped.pdf")), [3], new(), path, CollisionPolicy.Cancel); Reopen(path, 3);
        var before = await Render(At("cropped.pdf"), 3); var after = await Render(path, 3); var diff = Difference(before, after);
        Assert.True(diff.Right > diff.Left); Assert.True(diff.Top > before.PixelHeight * .7);
    }
    [Theory][InlineData(0, "valid")][InlineData(601, "valid")][InlineData(96, "bad:")][InlineData(96, "bad.")][InlineData(96, "bad ")]
    public async Task BadRasterOptionsAreRejectedBeforeOutput(int dpi, string prefix)
    {
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.RasterAsync(new(Pdf()), [1], new(Dpi: dpi, Prefix: prefix), directory, CollisionPolicy.Cancel));
        Assert.Empty(Directory.GetFiles(directory, "*.png"));
    }
    [Fact] public async Task ImageHeavyPdfReopensOverlaysAndExportsDecodableImages()
    {
        var random = new Random(813); var pixels = new byte[800 * 1000 * 4]; random.NextBytes(pixels); for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var bitmap = BitmapSource.Create(800, 1000, 96, 96, PixelFormats.Bgra32, null, pixels, 3200); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var imageBytes = new MemoryStream(); encoder.Save(imageBytes); imageBytes.Position = 0; using var image = XImage.FromStream(imageBytes);
        var path = At("image-heavy.pdf"); using (var doc = new PdfDocument())
        {
            for (var p = 0; p < 20; p++) { var page = doc.AddPage(); using var g = XGraphics.FromPdfPage(page); g.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point); }
            doc.Save(path);
        }
        var hash = Hash(path); var watch = Stopwatch.StartNew();
        await engine.WatermarkAsync(new(path), Enumerable.Range(1, 20).ToArray(), new("TEST", FontSize: 20), At("heavy-mark.pdf"), CollisionPolicy.Cancel); Reopen(At("heavy-mark.pdf"), 20);
        var results = await engine.RasterAsync(new(path), [1, 10, 20], new(RasterFormat.Jpeg, 72), directory, CollisionPolicy.Cancel);
        Assert.All(results, r => { Assert.Equal("Success", r.Status); Assert.True(Decode(r.Path).PixelWidth > 0); }); Assert.Equal(hash, Hash(path));
        output.WriteLine($"20 image-heavy pages: watermark plus three JPEG exports {watch.ElapsedMilliseconds} ms; peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MiB (includes fixture creation/test host).");
    }
    [Theory][InlineData(.25, 191)][InlineData(.75, 64)]
    public async Task ImageOpacityIsVisibleInDecodedPixels(double opacity, int expectedGreen)
    {
        var input = Pdf(); var path = At("opacity.pdf"); await engine.WatermarkAsync(new(input), [2], new(ImagePath: Image(), Opacity: opacity, Rotation: 0), path, CollisionPolicy.Cancel); Reopen(path, 3);
        var rendered = await Render(path, 2); var bytes = Pixels(rendered); var offset = ((rendered.PixelHeight / 2) * rendered.PixelWidth + rendered.PixelWidth / 2) * 4;
        Assert.InRange(bytes[offset + 1], expectedGreen - 5, expectedGreen + 5);
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}


