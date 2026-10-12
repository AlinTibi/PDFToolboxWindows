using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Almarfeld.PdfEngine;

internal static class PdfRendering
{
    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetScaleFactorForDevice(int device);
    public const long MaxPixels = 40_000_000;
    public static (uint Width, uint Height) Dimensions(Windows.Foundation.Size size, int dpi)
    {
        var w = Math.Round(size.Width * dpi / 96d); var h = Math.Round(size.Height * dpi / 96d);
        if (!double.IsFinite(w) || !double.IsFinite(h) || w < 1 || h < 1 || w > 16000 || h > 16000 || w * h > MaxPixels)
            throw new PdfOperationException("Limit", "Rendered pages are limited to 40 megapixels and 16,000 pixels per side. Reduce DPI.");
        return ((uint)w, (uint)h);
    }
    public static async Task<BitmapSource> RenderAsync(Windows.Data.Pdf.PdfDocument doc, int page, uint width, uint height, CancellationToken cancellation)
    {
        if (page < 1 || page > doc.PageCount) throw new PdfOperationException("Range", "Rendered page is out of range.");
        using var pdfPage = doc.GetPage((uint)page - 1);
        using var output = new InMemoryRandomAccessStream();
        var scale = Math.Clamp(GetScaleFactorForDevice(0) / 100d, 1, 5);
        var renderWidth = (uint)Math.Max(1, Math.Round(width / scale));
        var renderHeight = (uint)Math.Max(1, Math.Round(height / scale));
        await pdfPage.RenderToStreamAsync(output, new PdfPageRenderOptions
        { DestinationWidth = renderWidth, DestinationHeight = renderHeight, BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255) }).AsTask(cancellation);
        cancellation.ThrowIfCancellationRequested(); output.Seek(0);
        using var stream = output.AsStreamForRead();
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > MaxPixels + 100000 || frame.PixelWidth > 16005 || frame.PixelHeight > 16005)
            throw new PdfOperationException("Limit", "The Windows renderer exceeded the pixel limit. Reduce DPI.");
        stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        if (image.PixelWidth == width && image.PixelHeight == height) return image;
        // Windows may apply the display scale to render options in desktop processes.
        // Normalize to the requested pixel dimensions so exported resolution is independent of monitor DPI.
        var scaled = new TransformedBitmap(image, new ScaleTransform((double)width / image.PixelWidth, (double)height / image.PixelHeight)); scaled.Freeze(); return scaled;
    }
}

public sealed partial class PdfService
{
    public static void ValidateRaster(RasterOptions options)
    {
        if (!Enum.IsDefined(options.Format) || options.Dpi is < 36 or > 600 || options.Quality is < 10 or > 100 ||
            string.IsNullOrWhiteSpace(options.Prefix) || options.Prefix.Length > 100 || options.Prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            options.Prefix.EndsWith('.') || options.Prefix.EndsWith(' ') || options.Prefix is "." or "..")
            throw new PdfOperationException("Raster", "Use DPI 36–600, JPEG quality 10–100 and a valid filename prefix (at most 100 characters).");
    }
    public static string RasterName(RasterOptions options, int page) => $"{options.Prefix}-page-{page:000}.{(options.Format == RasterFormat.Png ? "png" : "jpg")}";
    public Task<IReadOnlyList<OutputResult>> RasterAsync(PdfInput input, IReadOnlyList<int> pages, RasterOptions options, string directory, CollisionPolicy policy,
        IProgress<JobProgress>? progress = null, CancellationToken cancellation = default, IReadOnlySet<string>? approvedReplacements = null) => Task.Run(async () =>
    {
        ValidateRaster(options);
        var info = await InspectAsync(input, cancellation);
        // Raster export would remove PDF encryption/permission restrictions; require an explicit Unlock operation first.
        if (info.Encrypted) throw new PdfOperationException("Encrypted", "Encrypted PDFs may be previewed with a valid password, but image export requires explicitly unlocking a copy with valid owner credentials first.");
        var selected = ValidateSelection(pages, info.PageCount);
        using var source = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var random = source.AsRandomAccessStream();
        var document = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(random).AsTask(cancellation);
        foreach (var number in selected)
        {
            using var p = document.GetPage((uint)number - 1); PdfRendering.Dimensions(p.Size, options.Dpi);
        }
        var results = new List<OutputResult>();
        foreach (var number in selected)
        {
            var target = Path.GetFullPath(Path.Combine(directory, RasterName(options, number)));
            if (cancellation.IsCancellationRequested) { results.Add(new(target, "Skipped", "Cancelled before this page.")); continue; }
            try
            {
                using var p = document.GetPage((uint)number - 1); var dimensions = PdfRendering.Dimensions(p.Size, options.Dpi);
                var effectivePolicy = policy == CollisionPolicy.Replace && (approvedReplacements == null || !approvedReplacements.Contains(target)) ? CollisionPolicy.Cancel : policy;
                var path = await SaveRasterPage(document, number, dimensions, options, target, effectivePolicy, input.Path, cancellation);
                results.Add(new(path, "Success", "Rendered page; source unchanged.", new FileInfo(path).Length));
            }
            catch (OperationCanceledException) { results.Add(new(target, "Skipped", "Cancelled; this page has no partial output.")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PdfOperationException or System.Runtime.InteropServices.COMException)
            { results.Add(new(target, "Failed", e.Message)); }
            progress?.Report(new(results.Count, selected.Length, "Rendering PDF pages"));
        }
        return (IReadOnlyList<OutputResult>)results;
    }, cancellation);

    private static async Task<string> SaveRasterPage(Windows.Data.Pdf.PdfDocument document, int number, (uint Width, uint Height) dimensions,
        RasterOptions options, string target, CollisionPolicy policy, string source, CancellationToken cancellation)
    {
        // Isolate page-local WIC objects. Encode the rendered frame directly instead of
        // copying through a converted bitmap, managed RGBA array and a second bitmap.
        var image = await PdfRendering.RenderAsync(document, number, dimensions.Width, dimensions.Height, cancellation);
        BitmapEncoder encoder = options.Format == RasterFormat.Png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = options.Quality };
        encoder.Frames.Add(BitmapFrame.Create(image));
        return SafeOutput.Commit(target, policy, [source], stream => { encoder.Save(stream); RasterDensity.Set(stream, options.Format, options.Dpi); }, temporary =>
        {
            using var verify = File.OpenRead(temporary);
            var decoded = BitmapDecoder.Create(verify, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];
            if (decoded.PixelWidth != dimensions.Width || decoded.PixelHeight != dimensions.Height)
                throw new PdfOperationException("Output", "Exported image dimensions did not validate; no final image was created.");
        }, cancellation);
    }
}

