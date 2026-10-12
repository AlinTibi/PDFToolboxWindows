using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Almarfeld.PdfEngine;

public sealed partial class PdfService
{
    public static void ValidateWatermark(WatermarkOptions options)
    {
        if (!Enum.IsDefined(options.Position) || !double.IsFinite(options.FontSize) || options.FontSize is < 6 or > 300 ||
            !double.IsFinite(options.Opacity) || options.Opacity is < 0 or > 1 || !double.IsFinite(options.Rotation) || Math.Abs(options.Rotation) > 360 ||
            !double.IsFinite(options.OffsetX) || Math.Abs(options.OffsetX) > 2000 || !double.IsFinite(options.OffsetY) || Math.Abs(options.OffsetY) > 2000 ||
            !double.IsFinite(options.Scale) || options.Scale is < .01 or > 2)
            throw new PdfOperationException("Watermark", "Use font size 6–300, opacity 0–1, rotation ±360°, offsets ±2000 pt and image scale 0.01–2.");
        if (options.ImagePath == null && (string.IsNullOrWhiteSpace(options.Text) || options.Text.Length > 500 || options.Text.Any(char.IsControl)))
            throw new PdfOperationException("Watermark", "Enter a single line of watermark text (up to 500 characters).");
        if (options.ImagePath != null && Path.GetExtension(options.ImagePath).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg"))
            throw new PdfOperationException("Watermark", "Image watermarks support PNG and JPEG.");
    }
    public static void ValidateNumbers(NumberOptions options)
    {
        if (options.Start < 0 || options.Start > 1_000_000 || options.Skip < 0 || options.Skip > MaxInputPages ||
            !double.IsFinite(options.FontSize) || options.FontSize is < 6 or > 72 || !double.IsFinite(options.Margin) || options.Margin is < 0 or > 144 ||
            !Enum.IsDefined(options.Format) || options.Position is not (OverlayPosition.TopLeft or OverlayPosition.TopCenter or OverlayPosition.TopRight or
                OverlayPosition.BottomLeft or OverlayPosition.BottomCenter or OverlayPosition.BottomRight) ||
            options.Prefix.Length + options.Suffix.Length > 120 || (options.Prefix + options.Suffix).Any(char.IsControl))
            throw new PdfOperationException("Numbers", "Use starting number 0–1,000,000, font 6–72 pt, margins 0–144 pt and a top/bottom position. Prefix/suffix must be single-line text.");
    }
    public static string NumberLabel(NumberOptions options, int ordinal, int count)
    {
        var number = checked(options.Start + ordinal);
        var last = checked(options.Start + count - 1);
        return options.Prefix + (options.Format switch
        {
            NumberFormat.Number => $"{number}", NumberFormat.PageNumber => $"Page {number}",
            NumberFormat.NumberOfTotal => $"{number} / {last}", _ => $"Page {number} of {last}"
        }) + options.Suffix;
    }
    public Task<OutputResult> WatermarkAsync(PdfInput input, IReadOnlyList<int> pages, WatermarkOptions options, string output, CollisionPolicy policy,
        IProgress<JobProgress>? progress = null, CancellationToken cancellation = default)
    {
        ValidateWatermark(options);
        return OverlayAsync(input, pages, options, null, output, policy, progress, cancellation);
    }
    public Task<OutputResult> NumbersAsync(PdfInput input, IReadOnlyList<int> pages, NumberOptions options, string output, CollisionPolicy policy,
        IProgress<JobProgress>? progress = null, CancellationToken cancellation = default)
    {
        ValidateNumbers(options);
        return OverlayAsync(input, pages, null, options, output, policy, progress, cancellation);
    }
    private Task<OutputResult> OverlayAsync(PdfInput input, IReadOnlyList<int> pages, WatermarkOptions? watermark, NumberOptions? numbers,
        string output, CollisionPolicy policy, IProgress<JobProgress>? progress, CancellationToken cancellation) => Task.Run(() =>
    {
        using var opened = new Opened(input, PdfDocumentOpenMode.Modify); Editable(opened);
        var selected = ValidateSelection(pages, opened.Document.PageCount, numbers?.Skip ?? 0);
        using var imageBytes = watermark?.ImagePath is { } imagePath ? WatermarkImage(imagePath, watermark.Opacity) : null;
        using var image = imageBytes == null ? null : XImage.FromStream(imageBytes);
        for (var i = 0; i < selected.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            DrawOverlay(opened.Document.Pages[selected[i] - 1], watermark, numbers, image, i, selected.Length);
            progress?.Report(new(i + 1, selected.Length, "Adding PDF content"));
        }
        return Save(opened.Document, output, policy, imageBytes == null ? [input.Path] : [input.Path, watermark!.ImagePath!], cancellation);
    }, cancellation);

    // The preview uses the exact export drawing code, on one imported page in memory.
    public Task<byte[]> OverlayPreviewAsync(PdfInput input, int page, IReadOnlyList<int> pages, WatermarkOptions? watermark, NumberOptions? numbers,
        CancellationToken cancellation = default) => Task.Run(() =>
    {
        if ((watermark == null) == (numbers == null)) throw new PdfOperationException("Preview", "Choose exactly one overlay tool.");
        if (watermark != null) ValidateWatermark(watermark); else ValidateNumbers(numbers!);
        using var opened = new Opened(input, PdfDocumentOpenMode.Import); Editable(opened);
        if (page < 1 || page > opened.Document.PageCount) throw new PdfOperationException("Range", "Preview page is out of range.");
        var selected = ValidateSelection(pages, opened.Document.PageCount, numbers?.Skip ?? 0);
        using var doc = new PdfDocument(); var copy = doc.AddPage(opened.Document.Pages[page - 1]);
        var ordinal = Array.IndexOf(selected, page);
        if (ordinal >= 0)
        {
            using var imageBytes = watermark?.ImagePath is { } path ? WatermarkImage(path, watermark.Opacity) : null;
            using var image = imageBytes == null ? null : XImage.FromStream(imageBytes);
            DrawOverlay(copy, watermark, numbers, image, ordinal, selected.Length);
        }
        cancellation.ThrowIfCancellationRequested(); using var output = new MemoryStream(); doc.Save(output, false); return output.ToArray();
    }, cancellation);

    internal static int[] ValidateSelection(IReadOnlyList<int> pages, int count, int skip = 0)
    {
        if (pages.Count == 0 || pages.Any(p => p < 1 || p > count) || pages.Distinct().Count() != pages.Count)
            throw new PdfOperationException("Range", "Choose existing pages without duplicates.");
        var selected = pages.Where(p => p > skip).Order().ToArray();
        if (selected.Length == 0) throw new PdfOperationException("Range", "No pages remain after excluding the first pages.");
        return selected;
    }
    private static MemoryStream WatermarkImage(string path, double opacity)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length is < 1 or > MaxInputBytes) throw new PdfOperationException("Limit", "Watermark image must be non-empty and at most 256 MiB.");
        var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 16_000_000) throw new PdfOperationException("Limit", "Watermark images are limited to 16 megapixels.");
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = checked(bitmap.PixelWidth * 4); var pixels = new byte[checked(stride * bitmap.PixelHeight)]; bitmap.CopyPixels(pixels, stride, 0);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = (byte)Math.Round(pixels[i] * opacity);
        var adjusted = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(adjusted));
        var result = new MemoryStream(); encoder.Save(result); result.Position = 0; return result;
    }
    private static void DrawOverlay(PdfPage page, WatermarkOptions? mark, NumberOptions? numbers, XImage? image, int ordinal, int count)
    {
        if (mark?.Opacity == 0) return;
        var media = page.MediaBox;
        if (media.X1 != 0 || media.Y1 != 0 || page.Elements.GetReal("/UserUnit") is not (0 or 1))
            throw new PdfOperationException("Geometry", "Overlay editing of non-zero media origins or custom UserUnit is not supported. Source retained.");
        var crop = page.Elements.ContainsKey("/CropBox") ? page.CropBox : page.MediaBox; var width = crop.Width; var height = crop.Height;
        var rotation = (page.Rotate % 360 + 360) % 360;
        if (rotation % 90 != 0) throw new PdfOperationException("Geometry", "Unsupported page rotation.");
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        // Map displayed crop-box coordinates back into the original unrotated PDF page.
        gfx.TranslateTransform(crop.X1, media.Y2 - crop.Y2);
        if (rotation == 90) { gfx.TranslateTransform(0, height); gfx.RotateTransform(-90); }
        if (rotation == 180) { gfx.TranslateTransform(width, height); gfx.RotateTransform(-180); }
        if (rotation == 270) { gfx.TranslateTransform(width, 0); gfx.RotateTransform(-270); }
        if (rotation is 90 or 270) (width, height) = (height, width);
        var position = mark?.Position ?? numbers!.Position;
        var margin = numbers?.Margin ?? 24;
        var font = new XFont("Segoe UI", mark?.FontSize ?? numbers!.FontSize);
        var text = mark?.Text ?? NumberLabel(numbers!, ordinal, count);
        var size = image == null ? gfx.MeasureString(text, font) : new XSize(width * mark!.Scale, width * mark.Scale * image.PixelHeight / image.PixelWidth);
        var angle = mark?.Rotation ?? 0;
        var radians = angle * Math.PI / 180;
        var boxW = Math.Abs(size.Width * Math.Cos(radians)) + Math.Abs(size.Height * Math.Sin(radians));
        var boxH = Math.Abs(size.Width * Math.Sin(radians)) + Math.Abs(size.Height * Math.Cos(radians));
        if (boxW + 2 * margin > width || boxH + 2 * margin > height)
            throw new PdfOperationException("Layout", "The overlay does not fit this page. Reduce its size, scale or margin.");
        var x = position is OverlayPosition.TopLeft or OverlayPosition.CenterLeft or OverlayPosition.BottomLeft ? margin + boxW / 2 :
            position is OverlayPosition.TopRight or OverlayPosition.CenterRight or OverlayPosition.BottomRight ? width - margin - boxW / 2 : width / 2;
        var y = position is OverlayPosition.TopLeft or OverlayPosition.TopCenter or OverlayPosition.TopRight ? margin + boxH / 2 :
            position is OverlayPosition.BottomLeft or OverlayPosition.BottomCenter or OverlayPosition.BottomRight ? height - margin - boxH / 2 : height / 2;
        x += mark?.OffsetX ?? 0; y += mark?.OffsetY ?? 0;
        if (x - boxW / 2 < 0 || y - boxH / 2 < 0 || x + boxW / 2 > width || y + boxH / 2 > height)
            throw new PdfOperationException("Layout", "The offsets place the overlay outside this page. Adjust the preview settings.");
        gfx.TranslateTransform(x, y); gfx.RotateTransform(angle);
        if (image != null) gfx.DrawImage(image, -size.Width / 2, -size.Height / 2, size.Width, size.Height);
        else gfx.DrawString(text, font, new XSolidBrush(XColor.FromArgb((int)Math.Round(255 * (mark?.Opacity ?? 1)), 24, 35, 50)),
            new XRect(-size.Width / 2, -size.Height / 2, size.Width, size.Height), XStringFormats.Center);
    }
}

