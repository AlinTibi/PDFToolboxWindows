using System.IO;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Windows.Data.Pdf;

namespace Almarfeld.PdfEngine;

public sealed class ThumbnailService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, BitmapSource> cache = [];
    private readonly Queue<string> order = new();
    public const long MaxCachedBytes = 24 * 1024 * 1024;
    public long CachedBytes { get; private set; }
    private static long Bytes(BitmapSource image) => (long)image.PixelWidth * image.PixelHeight * Math.Max(4, (image.Format.BitsPerPixel + 7) / 8);
    public int CachedCount => cache.Count;
    public async Task<BitmapSource> GetAsync(PdfInput input, int page, CancellationToken cancellation = default)
        => await PreviewAsync(input, page, 240, "source", null, cancellation);

    public async Task<BitmapSource> PreviewAsync(PdfInput input, int page, uint width, string variant,
        Func<CancellationToken, Task<byte[]>>? modifiedPdf = null, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var info = new FileInfo(input.Path);
            width = Math.Clamp(width, 120u, 1200u);
            var key = $"{input.SessionId}|{input.Path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{page}|{width}|{variant}";
            if (cache.TryGetValue(key, out var existing)) return existing;
            using Stream file = modifiedPdf == null ? new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read)
                : new MemoryStream(await modifiedPdf(cancellation));
            using var random = file.AsRandomAccessStream();
            var document = await PdfDocument.LoadFromStreamAsync(random, modifiedPdf == null ? input.Password ?? "" : "").AsTask(cancellation);
            var renderedPage = modifiedPdf == null ? page : 1;
            if (renderedPage < 1 || renderedPage > document.PageCount) throw new PdfOperationException("Range", "Preview page is out of range.");
            using var pdfPage = document.GetPage((uint)renderedPage - 1);
            var ratio = pdfPage.Size.Height / Math.Max(1, pdfPage.Size.Width);
            if (width * ratio > 1600) width = (uint)Math.Max(1, Math.Floor(1600 / ratio));
            var height = (uint)Math.Clamp(width * ratio, 1, 1600);
            var image = await PdfRendering.RenderAsync(document, renderedPage, width, height, cancellation);
            cancellation.ThrowIfCancellationRequested();
            while (cache.Count > 0 && (cache.Count >= 16 || CachedBytes + Bytes(image) > MaxCachedBytes))
            {
                var oldest = order.Dequeue(); CachedBytes -= Bytes(cache[oldest]); cache.Remove(oldest);
            }
            cache.Add(key, image); order.Enqueue(key);
            CachedBytes += Bytes(image);
            return image;
        }
        finally { gate.Release(); }
    }
    public async Task ClearAsync()
    {
        await gate.WaitAsync();
        try { cache.Clear(); order.Clear(); CachedBytes = 0; } finally { gate.Release(); }
    }
}
