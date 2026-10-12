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
    public int CachedCount => cache.Count;
    public async Task<BitmapSource> GetAsync(PdfInput input, int page, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var info = new FileInfo(input.Path);
            var key = $"{input.Path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{page}";
            if (cache.TryGetValue(key, out var existing)) return existing;
            using var file = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var random = file.AsRandomAccessStream();
            var document = await PdfDocument.LoadFromStreamAsync(random, input.Password ?? "").AsTask(cancellation);
            if (page < 1 || page > document.PageCount) throw new PdfOperationException("Range", "Preview page is out of range.");
            using var pdfPage = document.GetPage((uint)page - 1);
            using var output = new InMemoryRandomAccessStream();
            var ratio = pdfPage.Size.Height / Math.Max(1, pdfPage.Size.Width);
            await pdfPage.RenderToStreamAsync(output, new PdfPageRenderOptions { DestinationWidth = 240, DestinationHeight = (uint)Math.Clamp(240 * ratio, 1, 480) }).AsTask(cancellation);
            output.Seek(0);
            using var stream = output.AsStreamForRead();
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            if (cache.Count >= 16) cache.Remove(order.Dequeue());
            cache.Add(key, image); order.Enqueue(key);
            return image;
        }
        finally { gate.Release(); }
    }
    public async Task ClearAsync()
    {
        await gate.WaitAsync();
        try { cache.Clear(); order.Clear(); } finally { gate.Release(); }
    }
}
