using System.Diagnostics;
using System.IO;
using Almarfeld.PdfEngine;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using Xunit;
using Xunit.Abstractions;

namespace PdfEngine.Tests;
public sealed class MemoryTests(ITestOutputHelper log)
{
    [Fact]
    public async Task PreviewCacheIsByteBoundedEvictsAndClearsOnCancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PDFToolboxCache", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "120.pdf"); using (var doc = new PdfDocument()) { for (var i = 0; i < 120; i++) doc.AddPage(); doc.Save(path); }
            var input = new PdfInput(path); var previews = new ThumbnailService();
            var first = await previews.PreviewAsync(input, 1, 900, "stress");
            for (var p = 2; p <= 40; p++) { await previews.PreviewAsync(input, p, 900, "stress"); Assert.InRange(previews.CachedBytes, 0, ThumbnailService.MaxCachedBytes); Assert.InRange(previews.CachedCount, 1, 16); }
            Assert.NotSame(first, await previews.PreviewAsync(input, 1, 900, "stress"));
            for (var open = 0; open < 10; open++) { await previews.ClearAsync(); await previews.GetAsync(new(path), 1); Assert.Equal(1, previews.CachedCount); }
            await previews.ClearAsync(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => previews.GetAsync(input, 1, cancel.Token));
            Assert.Equal(0, previews.CachedBytes); Assert.Equal(0, previews.CachedCount);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory][InlineData(300, 12)][InlineData(600, 12)][InlineData(600, 120)]
    public async Task SequentialHighResolutionExport(int dpi, int pages)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PDFToolboxMemory", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.pdf");
            using (var doc = new PdfDocument()) { for (var i = 0; i < pages; i++) { var page = doc.AddPage(); using var g = XGraphics.FromPdfPage(page); g.DrawRectangle(XBrushes.Navy, 20, 20, 400, 500); } doc.Save(source); }
            var process = Process.GetCurrentProcess(); var watch = Stopwatch.StartNew();
            var results = await new PdfService().RasterAsync(new(source), Enumerable.Range(1, pages).ToArray(), new(RasterFormat.Jpeg, dpi), directory, CollisionPolicy.Cancel);
            Assert.All(results, r => Assert.Equal("Success", r.Status));
            foreach (var result in results) { using var file = File.OpenRead(result.Path); var image = System.Windows.Media.Imaging.BitmapDecoder.Create(file, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.None).Frames[0]; Assert.InRange(image.DpiX, dpi - .03, dpi + .03); }
            process.Refresh(); log.WriteLine($"DPI={dpi}; pages={pages}; peak={process.PeakWorkingSet64 / 1048576} MiB; private={process.PrivateMemorySize64 / 1048576} MiB; elapsed={watch.ElapsedMilliseconds} ms");
        }
        finally { Directory.Delete(directory, true); }
    }
}
