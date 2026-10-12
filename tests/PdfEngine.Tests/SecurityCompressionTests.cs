using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Almarfeld.PdfEngine;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;
using Xunit;
using Xunit.Abstractions;

namespace PdfEngine.Tests;
public sealed class SecurityCompressionTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "PDFToolboxSecurity", Guid.NewGuid().ToString("N"));
    private readonly PdfService engine = new(); private readonly ITestOutputHelper log;
    public SecurityCompressionTests(ITestOutputHelper log) { this.log = log; Directory.CreateDirectory(dir); }
    private string At(string file) => Path.Combine(dir, file);
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private string Fixture(string kind = "text")
    {
        var path = At(kind + ".pdf"); using var doc = new PdfDocument(); doc.Info.Title = "Synthetic validation";
        doc.Options.CompressContentStreams = kind == "optimized";
        var count = kind == "large" ? 120 : 3;
        for (var i = 0; i < count; i++)
        {
            var page = doc.AddPage(); if (kind == "rotated") { page.Width = XUnit.FromPoint(840); page.Height = XUnit.FromPoint(595); page.Rotate = 90; }
            using (var g = XGraphics.FromPdfPage(page))
            {
                if (kind != "image") for (var n = 0; n < 150; n++) g.DrawString("Synthetic searchable text " + n, new XFont("Arial", 10), XBrushes.Navy, 20, 20 + n % 70 * 10);
                if (kind is "image" or "mixed")
                {
                    var pixels = new byte[320 * 240 * 3]; Array.Fill(pixels, (byte)122);
                    var bitmap = BitmapSource.Create(320, 240, 96, 96, PixelFormats.Rgb24, null, pixels, 960);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var bytes = new MemoryStream(); encoder.Save(bytes); bytes.Position = 0;
                    using var image = XImage.FromStream(bytes); g.DrawImage(image, 20, 20, 300, 200);
                }
            }
            var annotation = new PdfDictionary(doc); annotation.Elements.SetName("/Type", "/Annot"); annotation.Elements.SetName("/Subtype", "/Text");
            annotation.Elements.SetString("/Contents", "Synthetic annotation"); annotation.Elements["/Rect"] = new PdfArray(doc, new PdfInteger(20), new PdfInteger(20), new PdfInteger(40), new PdfInteger(40));
            doc.Internals.AddObject(annotation); page.Elements["/Annots"] = new PdfArray(doc, annotation.Reference!);
        }
        if (kind == "form")
        {
            var field = new PdfDictionary(doc); field.Elements.SetName("/FT", "/Tx"); field.Elements.SetString("/T", "SyntheticField"); field.Elements.SetString("/V", "Retained value"); doc.Internals.AddObject(field);
            var form = new PdfDictionary(doc); form.Elements["/Fields"] = new PdfArray(doc, field.Reference!); doc.Internals.AddObject(form); doc.Internals.Catalog.Elements["/AcroForm"] = form.Reference!;
        }
        doc.Save(path); return path;
    }
    private static byte[] PageContent(PdfDocument doc, int page) => doc.Pages[page].Contents.Elements.Cast<PdfReference>().SelectMany(r => ((PdfDictionary)r.Value).Stream.UnfilteredValue).ToArray();

    [Theory][InlineData("text")][InlineData("form")][InlineData("rotated")] public async Task ProtectUnlockRoundTripVerifiesAESAndContent(string kind)
    {
        var source = Fixture(kind); var hash = Hash(source); var protectedPath = At("protected.pdf"); var password = "synthetic-test-password";
        await engine.ProtectAsync(new(source), password, password, protectedPath, CollisionPolicy.Cancel);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.InspectAsync(new(protectedPath)));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.InspectAsync(new(protectedPath, "incorrect")));
        using (var before = PdfReader.Open(source, PdfDocumentOpenMode.Import))
        using (var secured = PdfReader.Open(protectedPath, password, PdfDocumentOpenMode.Import))
        {
            Assert.Equal(3, secured.PageCount); Assert.Equal(PageContent(before, 0), PageContent(secured, 0));
            Assert.Equal(4, secured.SecurityHandler.Elements.GetInteger("/V")); Assert.Equal(4, secured.SecurityHandler.Elements.GetInteger("/R"));
            Assert.Contains("/AESV2", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(protectedPath)));
        }
        var encryptedHash = Hash(protectedPath);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.UnlockAsync(new(protectedPath), "incorrect", At("wrong.pdf"), CollisionPolicy.Cancel));
        await engine.UnlockAsync(new(protectedPath), password, At("unlocked.pdf"), CollisionPolicy.Cancel);
        using var unlocked = PdfReader.Open(At("unlocked.pdf"), PdfDocumentOpenMode.Import); using var original = PdfReader.Open(source, PdfDocumentOpenMode.Import);
        Assert.Equal(3, unlocked.PageCount); Assert.Equal(PageContent(original, 0), PageContent(unlocked, 0));
        Assert.False((await engine.InspectAsync(new(At("unlocked.pdf")))).Encrypted);
        Assert.Equal(hash, Hash(source)); Assert.Equal(encryptedHash, Hash(protectedPath)); Assert.False(File.Exists(At("wrong.pdf")));
    }
    [Theory][InlineData("", "")][InlineData("different", "other")][InlineData("é", "é")]
    public async Task InvalidProtectionPasswordsCreateNothing(string password, string confirm)
    {
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ProtectAsync(new(Fixture()), password, confirm, At("bad.pdf"), CollisionPolicy.Cancel));
        Assert.False(File.Exists(At("bad.pdf")));
    }
    [Fact] public async Task UnlockDoesNotBypassOwnerRestrictions()
    {
        var path = Fixture(); using (var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify)) { doc.SecuritySettings.UserPassword = "reader"; doc.SecuritySettings.OwnerPassword = "owner"; doc.Save(At("restricted.pdf")); }
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.UnlockAsync(new(At("restricted.pdf")), "reader", At("bad.pdf"), CollisionPolicy.Cancel));
        await engine.UnlockAsync(new(At("restricted.pdf")), "owner", At("good.pdf"), CollisionPolicy.Cancel);
    }
    [Fact] public async Task UnlockUnencryptedAndEmptyPasswordRefused()
    {
        var input = new PdfInput(Fixture());
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.UnlockAsync(input, "anything", At("bad.pdf"), CollisionPolicy.Cancel));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.UnlockAsync(input, "", At("bad.pdf"), CollisionPolicy.Cancel));
    }
    [Theory][InlineData("text")][InlineData("image")][InlineData("mixed")][InlineData("optimized")][InlineData("large")][InlineData("rotated")][InlineData("form")]
    public async Task CompressionMeasuresSavingsAndPreservesContent(string kind)
    {
        var source = Fixture(kind); var hash = Hash(source); var result = await engine.CompressAsync(new(source), At("output.pdf"), CollisionPolicy.Cancel);
        log.WriteLine($"{kind}: original={result.OriginalBytes}; output={result.OutputBytes}; saved={result.SavedBytes} ({result.SavedPercent:F2}%); {result.Status}");
        if (result.Path != null)
        {
            Assert.True(result.OutputBytes < result.OriginalBytes);
            using var before = PdfReader.Open(source, PdfDocumentOpenMode.Import); using var after = PdfReader.Open(result.Path, PdfDocumentOpenMode.Import);
            Assert.Equal(before.PageCount, after.PageCount); Assert.Equal(before.Info.Title, after.Info.Title);
            for (var p = 0; p < before.PageCount; p++) { Assert.Equal(PageContent(before, p), PageContent(after, p)); Assert.Equal(before.Pages[p].Rotate, after.Pages[p].Rotate); Assert.Equal(before.Pages[p].Elements.GetArray("/Annots")!.Elements.Count, after.Pages[p].Elements.GetArray("/Annots")!.Elements.Count); }
            Assert.Equal(before.Internals.Catalog.Elements.ContainsKey("/AcroForm"), after.Internals.Catalog.Elements.ContainsKey("/AcroForm"));
        }
        else { Assert.True(result.OutputBytes >= result.OriginalBytes); Assert.False(File.Exists(At("output.pdf"))); }
        Assert.Equal(hash, Hash(source)); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
    [Fact] public async Task OutputSafetyUnicodeCancellationAndLockedDestination()
    {
        var input = new PdfInput(Fixture()); var hash = Hash(input.Path); var path = At("コピー ș nou.pdf");
        await engine.ProtectAsync(input, "test", "test", path, CollisionPolicy.Cancel);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => engine.ProtectAsync(input, "test", "test", path, CollisionPolicy.Replace));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.CompressAsync(input, At("cancel.pdf"), CollisionPolicy.Cancel, cancellation: cancel.Token));
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.ProtectAsync(input, "test", "test", input.Path, CollisionPolicy.Replace));
        Assert.False(File.Exists(At("cancel.pdf"))); Assert.Equal(hash, Hash(input.Path)); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
    [Fact] public async Task CompressionRefusesEncryptedInput()
    {
        var source = Fixture(); await engine.ProtectAsync(new(source), "test", "test", At("encrypted.pdf"), CollisionPolicy.Cancel);
        await Assert.ThrowsAsync<PdfOperationException>(() => engine.CompressAsync(new(At("encrypted.pdf"), "test"), At("bad.pdf"), CollisionPolicy.Cancel));
    }
    [Fact] public async Task CompressionCancellationDuringWorkAndLockedOutputLeaveNoStage()
    {
        var source = Fixture("large"); var hash = Hash(source); using var cancel = new CancellationTokenSource();
        var progress = new CancelProgress(cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.CompressAsync(new(source), At("cancel.pdf"), CollisionPolicy.Cancel, progress, cancel.Token));
        File.WriteAllText(At("locked.pdf"), "Existing output"); var existing = Hash(At("locked.pdf"));
        using (var locked = new FileStream(At("locked.pdf"), FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => engine.CompressAsync(new(source), At("locked.pdf"), CollisionPolicy.Replace));
        Assert.Equal(existing, Hash(At("locked.pdf"))); Assert.Equal(hash, Hash(source)); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<JobProgress> { public void Report(JobProgress value) => cancellation.Cancel(); }
    [Fact] public async Task LongUnicodePathProtectUnlockRetainsSource()
    {
        var source = Fixture(); var hash = Hash(source); var longDir = dir;
        for (var i = 0; i < 5; i++) longDir = Path.Combine(longDir, "synthetic-long-path-ș-" + new string('x', 40));
        Directory.CreateDirectory(longDir); var secured = Path.Combine(longDir, "protected.pdf"); var plain = Path.Combine(longDir, "unlocked.pdf");
        await engine.ProtectAsync(new(source), "test", "test", secured, CollisionPolicy.Cancel);
        await engine.UnlockAsync(new(secured), "test", plain, CollisionPolicy.Cancel);
        Assert.Equal(3, (await engine.InspectAsync(new(plain))).PageCount); Assert.Equal(hash, Hash(source));
    }
    [Fact] public async Task ExistingOutputConflictAndProtectCancellationKeepBytes()
    {
        var source = Fixture(); var path = At("existing.pdf"); File.WriteAllText(path, "Synthetic existing output"); var hash = Hash(path);
        await Assert.ThrowsAnyAsync<IOException>(() => engine.ProtectAsync(new(source), "test", "test", path, CollisionPolicy.Cancel));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ProtectAsync(new(source), "test", "test", At("cancel.pdf"), CollisionPolicy.Cancel, cancel.Token));
        Assert.Equal(hash, Hash(path)); Assert.False(File.Exists(At("cancel.pdf"))); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
    [Fact] public void SimulatedWriteFailureCleansTemporaryAndPreservesDestination()
    {
        var source = Fixture(); File.WriteAllText(At("target.pdf"), "Synthetic output"); var hash = Hash(At("target.pdf"));
        Assert.Throws<IOException>(() => SafeOutput.Commit(At("target.pdf"), CollisionPolicy.Replace, [source], stream => { stream.WriteByte(1); throw new IOException("Synthetic write failure"); }, _ => { }, CancellationToken.None));
        Assert.Equal(hash, Hash(At("target.pdf"))); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
    public void Dispose() => Directory.Delete(dir, true);
}
