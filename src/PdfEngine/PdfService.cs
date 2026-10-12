using PdfSharp.Pdf;
using System.IO;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Almarfeld.PdfEngine;

public sealed partial class PdfService
{
    public const long MaxInputBytes = 256L * 1024 * 1024;
    public const int MaxInputPages = 5000, MaxOutputPages = 10000, MaxImages = 500;

    private sealed class Opened : IDisposable
    {
        public FileStream Stream { get; }
        public PdfDocument Document { get; }
        public bool Encrypted { get; }
        public Opened(PdfInput input, PdfDocumentOpenMode mode)
        {
            Stream = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (Stream.Length is < 5 or > MaxInputBytes) throw new PdfOperationException("Limit", "PDF inputs must be non-empty and no larger than 256 MiB.");
                var header = new byte[(int)Math.Min(1024, Stream.Length)]; Stream.ReadExactly(header); Stream.Position = 0;
                if (!System.Text.Encoding.ASCII.GetString(header).Contains("%PDF-", StringComparison.Ordinal)) throw new PdfOperationException("Invalid", "The file does not contain a PDF header.");
                try { Document = PdfReader.Open(Stream, input.Password, mode); }
                catch (PdfReaderException e) when (e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
                { throw new PdfOperationException("Password", "This encrypted PDF needs a valid password (owner password if editing is restricted)."); }
                catch (PdfReaderException) { throw new PdfOperationException("Invalid", "The PDF could not be parsed. It may be malformed or use unsupported PDF features."); }
                // PDFsharp resets the encryption mode after reading, but retains the parsed
                // security-handler object. Inspect that object rather than the output mode.
                Encrypted = Document.Internals.GetAllObjects().Any(x => x is PdfSharp.Pdf.Security.PdfStandardSecurityHandler);
                if (Document.PageCount is < 1 or > MaxInputPages) throw new PdfOperationException("Limit", "PDF inputs must contain 1–5,000 pages.");
            }
            catch { Document?.Dispose(); Stream.Dispose(); throw; }
        }
        public void Dispose() { Document.Dispose(); Stream.Dispose(); }
    }

    private static bool Signed(PdfDocument document) => document.Internals.GetAllObjects().OfType<PdfDictionary>().Any(x =>
        x.Elements.GetName("/Type") == "/Sig" || x.Elements.GetName("/FT") == "/Sig");
    private static bool Forms(PdfDocument document) => document.Internals.Catalog.Elements.ContainsKey("/AcroForm");
    private static void Editable(Opened opened)
    {
        var document = opened.Document;
        if (Signed(document)) throw new PdfOperationException("Signed", "Signed PDFs are not edited in this milestone; rewriting would invalidate their signatures.");
        if (Forms(document)) throw new PdfOperationException("Forms", "Interactive forms are not supported for editing in this milestone.");
        if (opened.Encrypted)
            throw new PdfOperationException("Encrypted", "Encrypted PDFs can be inspected with a valid password. Use Unlock with valid owner credentials to create an unencrypted copy before editing; encryption is never removed silently.");
    }
    public Task<PdfInfo> InspectAsync(PdfInput input, CancellationToken cancellation = default) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        using var opened = new Opened(input, PdfDocumentOpenMode.Import); var doc = opened.Document;
        var info = new PdfInfo(input.Path, doc.PageCount, opened.Stream.Length, opened.Encrypted, Signed(doc), Forms(doc),
            new(doc.Info.Title, doc.Info.Author, doc.Info.Subject, doc.Info.Keywords, doc.Info.Creator, doc.Info.Elements.GetString("/Producer")), doc.Pages.Cast<PdfPage>().Select(x => x.Rotate).ToArray());
        cancellation.ThrowIfCancellationRequested(); return info;
    }, cancellation);

    public Task<OutputResult> ExportPagesAsync(IReadOnlyList<PdfInput> inputs, IReadOnlyList<PageSelection> pages, string output, CollisionPolicy policy,
        IProgress<JobProgress>? progress = null, CancellationToken cancellation = default) => Task.Run(() =>
    {
        if (inputs.Count == 0 || pages.Count == 0) throw new PdfOperationException("Empty", "Choose input files and at least one output page.");
        if (inputs.Count > 100 || inputs.Sum(x => new FileInfo(x.Path).Length) > 512L * 1024 * 1024)
            throw new PdfOperationException("Limit", "A PDF operation supports up to 100 inputs and 512 MiB of combined input files.");
        if (pages.Count > MaxOutputPages) throw new PdfOperationException("Limit", "The output exceeds 10,000 pages.");
        var opened = new Dictionary<int, Opened>();
        try
        {
            using var result = new PdfDocument();
            for (var i = 0; i < pages.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested(); var pick = pages[i];
                if (pick.Source < 0 || pick.Source >= inputs.Count || pick.Rotation % 90 != 0) throw new PdfOperationException("Selection", "Invalid source or rotation.");
                if (!opened.TryGetValue(pick.Source, out var document)) { document = new Opened(inputs[pick.Source], PdfDocumentOpenMode.Import); opened.Add(pick.Source, document); Editable(document); }
                if (pick.Page < 1 || pick.Page > document.Document.PageCount) throw new PdfOperationException("Range", "The selected page no longer exists. Reopen the source.");
                var page = result.AddPage(document.Document.Pages[pick.Page - 1]);
                page.Rotate = ((page.Rotate + pick.Rotation) % 360 + 360) % 360;
                progress?.Report(new(i + 1, pages.Count, "Preparing pages"));
            }
            return Save(result, output, policy, inputs.Select(x => x.Path), cancellation);
        }
        finally { foreach (var document in opened.Values) document.Dispose(); }
    }, cancellation);

    public async Task<IReadOnlyList<OutputResult>> SplitAsync(PdfInput input, SplitMode mode, string ranges, int everyN, string directory, string baseName,
        CollisionPolicy policy, IProgress<JobProgress>? progress = null, CancellationToken cancellation = default)
    {
        if (policy == CollisionPolicy.Replace) throw new PdfOperationException("Output", "Split uses Rename or Cancel; replacing several existing outputs is not supported.");
        if (string.IsNullOrWhiteSpace(baseName) || baseName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || baseName.EndsWith('.') || baseName.EndsWith(' '))
            throw new PdfOperationException("Output", "Choose a valid output base filename.");
        var info = await InspectAsync(input, cancellation);
        var groups = PageRanges.Split(mode, ranges, info.PageCount, everyN);
        // All selections are validated before creating any output. Results remain honest if a later write fails.
        var results = new List<OutputResult>();
        for (var i = 0; i < groups.Count; i++)
        {
            var target = System.IO.Path.Combine(directory, $"{baseName}-part-{i + 1:000}.pdf");
            if (cancellation.IsCancellationRequested) { results.Add(new(target, "Skipped", "Cancelled before this part.")); continue; }
            try { results.Add(await ExportPagesAsync([input], groups[i].Select(p => new PageSelection(0, p)).ToArray(), target, policy, cancellation: cancellation)); }
            catch (OperationCanceledException) { results.Add(new(target, "Skipped", "Cancelled; no partial output retained for this part.")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PdfOperationException) { results.Add(new(target, "Failed", e.Message)); }
            progress?.Report(new(i + 1, groups.Count, "Exporting split parts"));
        }
        return results;
    }

    public Task<OutputResult> MetadataAsync(PdfInput input, PdfMetadata metadata, string output, CollisionPolicy policy, CancellationToken cancellation = default) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested(); using var opened = new Opened(input, PdfDocumentOpenMode.Modify); var doc = opened.Document; Editable(opened);
        doc.Info.Title = metadata.Title; doc.Info.Author = metadata.Author; doc.Info.Subject = metadata.Subject; doc.Info.Keywords = metadata.Keywords;
        doc.Info.Creator = metadata.Creator; doc.Info.Elements.SetString("/Producer", metadata.Producer);
        // The library may update Producer while saving. Metadata editing is not a sanitization claim.
        return Save(doc, output, policy, [input.Path], cancellation);
    }, cancellation);

    public Task<OutputResult> ImagesAsync(IReadOnlyList<string> images, ImageOptions options, string output, CollisionPolicy policy,
        IProgress<JobProgress>? progress = null, CancellationToken cancellation = default) => Task.Run(() =>
    {
        if (images.Count is < 1 or > MaxImages) throw new PdfOperationException("Limit", "Choose between 1 and 500 images.");
        if (!double.IsFinite(options.Margin) || options.Margin < 0 || options.Margin > 144 || options.Quality is < 10 or > 100 || options.PageSize is not ("A4" or "Letter"))
            throw new PdfOperationException("Image", "Choose A4/Letter, margins of 0–144 points and JPEG quality of 10–100.");
        using var doc = new PdfDocument();
        var sources = new List<FileStream>();
        try
        {
            for (var i = 0; i < images.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (System.IO.Path.GetExtension(images[i]).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png" or ".bmp")) throw new PdfOperationException("Image", "This milestone supports JPG, JPEG, PNG and BMP. WEBP is not enabled.");
                var stream = new FileStream(images[i], FileMode.Open, FileAccess.Read, FileShare.Read); sources.Add(stream);
                if (stream.Length is < 1 or > MaxInputBytes) throw new PdfOperationException("Limit", "An image is empty or exceeds 256 MiB.");
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); var frame = decoder.Frames[0];
                if ((long)frame.PixelWidth * frame.PixelHeight > 40_000_000) throw new PdfOperationException("Limit", "An image exceeds the 40-megapixel limit.");
                // Encode one image at a time, bounded by the pixel limit; opaque JPEG quality is explicit.
                BitmapEncoder encoder;
                if (frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Pbgra32 || frame.Format == PixelFormats.Indexed8)
                    encoder = new PngBitmapEncoder();
                else encoder = new JpegBitmapEncoder { QualityLevel = options.Quality };
                encoder.Frames.Add(BitmapFrame.Create(frame)); using var encoded = new MemoryStream(); encoder.Save(encoded); encoded.Position = 0;
                using var image = XImage.FromStream(encoded);
                var page = doc.AddPage(); page.Width = XUnit.FromPoint(options.PageSize == "A4" ? 595.276 : 612); page.Height = XUnit.FromPoint(options.PageSize == "A4" ? 841.89 : 792);
                if (options.Landscape) { var width = page.Width; page.Width = page.Height; page.Height = width; }
                var w = page.Width.Point - 2 * options.Margin; var h = page.Height.Point - 2 * options.Margin;
                var ratio = options.Fit == ImageFit.Fit ? Math.Min(w / frame.PixelWidth, h / frame.PixelHeight) : Math.Max(w / frame.PixelWidth, h / frame.PixelHeight);
                using var gfx = XGraphics.FromPdfPage(page); gfx.IntersectClip(new XRect(options.Margin, options.Margin, w, h));
                var dw = frame.PixelWidth * ratio; var dh = frame.PixelHeight * ratio;
                gfx.DrawImage(image, options.Margin + (w - dw) / 2, options.Margin + (h - dh) / 2, dw, dh);
                progress?.Report(new(i + 1, images.Count, "Placing images"));
            }
            return Save(doc, output, policy, images, cancellation);
        }
        finally { foreach (var stream in sources) stream.Dispose(); }
    }, cancellation);

    private static OutputResult Save(PdfDocument doc, string target, CollisionPolicy policy, IEnumerable<string> inputs, CancellationToken cancellation)
    {
        var pages = doc.PageCount;
        var path = SafeOutput.Commit(target, policy, inputs, stream => doc.Save(stream, false), temporary =>
        {
            using var verify = PdfReader.Open(temporary, PdfDocumentOpenMode.Import);
            if (verify.PageCount != pages) throw new PdfOperationException("Output", "Output validation failed; no final PDF was created.");
        }, cancellation);
        return new(path, "Success", "Exported copy; sources unchanged.", new FileInfo(path).Length);
    }
}
