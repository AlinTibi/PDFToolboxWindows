using System.IO.Compression;
using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Almarfeld.PdfEngine;

public sealed record CompressionResult(string? Path, long OriginalBytes, long OutputBytes, string Status)
{
    public long SavedBytes => OriginalBytes - OutputBytes;
    public double SavedPercent => 100d * SavedBytes / Math.Max(1, OriginalBytes);
}

public sealed partial class PdfService
{
    public static void ValidateProtection(string password, string confirmation)
    {
        // AES-128/PDF 1.6 passwords are byte-oriented and limited to 32 bytes.
        // Avoid silently truncated or ambiguously encoded credentials.
        if (string.IsNullOrEmpty(password) || password.Length > 32 || password.Any(c => c < 32 || c > 126))
            throw new PdfOperationException("Password", "Use 1–32 printable ASCII characters for PDF AES-128 compatibility.");
        if (password != confirmation) throw new PdfOperationException("Password", "The password confirmation does not match.");
    }

    private static void Rewritable(Opened opened, bool encryptedAllowed)
    {
        if (Signed(opened.Document)) throw new PdfOperationException("Signed", "Rewriting a digitally signed PDF would invalidate its signatures and is not supported.");
        if (opened.Encrypted && !encryptedAllowed) throw new PdfOperationException("Encrypted", "Unlock this PDF with valid credentials before protecting or compressing a copy.");
        // PdfReader Modify mode enforces owner credentials before returning a document.
    }

    public Task<OutputResult> ProtectAsync(PdfInput input, string password, string confirmation, string output, CollisionPolicy policy,
        CancellationToken cancellation = default) => Task.Run(() =>
    {
        ValidateProtection(password, confirmation); cancellation.ThrowIfCancellationRequested();
        using var opened = new Opened(input, PdfDocumentOpenMode.Modify); Rewritable(opened, false);
        var doc = opened.Document;
        doc.SecuritySettings.UserPassword = password; doc.SecuritySettings.OwnerPassword = password;
        doc.SecurityHandler.SetEncryptionToV4UsingAES();
        try { return SaveSecurity(doc, input, output, policy, password, true, cancellation); }
        finally { doc.SecurityHandler.SetEncryptionToNoneAndResetPasswords(); }
    }, cancellation);

    public Task<OutputResult> UnlockAsync(PdfInput input, string password, string output, CollisionPolicy policy,
        CancellationToken cancellation = default) => Task.Run(() =>
    {
        if (string.IsNullOrEmpty(password)) throw new PdfOperationException("Password", "Enter the correct PDF password. Restricted PDFs require the owner password.");
        cancellation.ThrowIfCancellationRequested();
        using var opened = new Opened(new(input.Path, password), PdfDocumentOpenMode.Modify);
        if (!opened.Encrypted) throw new PdfOperationException("Encrypted", "This PDF is not encrypted; no unlocking is needed.");
        Rewritable(opened, true); opened.Document.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
        return SaveSecurity(opened.Document, input, output, policy, null, false, cancellation);
    }, cancellation);

    private static OutputResult SaveSecurity(PdfDocument doc, PdfInput input, string output, CollisionPolicy policy, string? password, bool encrypted,
        CancellationToken cancellation)
    {
        var count = doc.PageCount;
        var path = SafeOutput.Commit(output, policy, [input.Path], stream => doc.Save(stream, false), temp =>
        {
            using var verify = new Opened(new(temp, password), PdfDocumentOpenMode.Import);
            if (verify.Document.PageCount != count || verify.Encrypted != encrypted)
                throw new PdfOperationException("Output", "The output security/page validation failed; no final PDF was created.");
        }, cancellation);
        return new(path, "Success", encrypted ? "Password-protected copy (AES-128); source unchanged." : "Unlocked copy; source unchanged.", new FileInfo(path).Length);
    }

    public Task<CompressionResult> CompressAsync(PdfInput input, string output, CollisionPolicy policy,
        IProgress<JobProgress>? progress = null, CancellationToken cancellation = default) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested(); using var opened = new Opened(input, PdfDocumentOpenMode.Modify); Rewritable(opened, false);
        var doc = opened.Document; var original = opened.Stream.Length; var pageCount = doc.PageCount; var forms = Forms(doc); long size = 0;
        doc.Options.CompressContentStreams = true; doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        var objects = doc.Internals.GetAllObjects();
        for (var i = 0; i < objects.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            // Lossless only: do not decode/recompress images or unknown existing filters.
            if (objects[i] is PdfDictionary dictionary && dictionary.Stream is { } stream &&
                !dictionary.Elements.ContainsKey("/Filter") && !dictionary.Elements.ContainsKey("/DecodeParms") && !dictionary.Elements.ContainsKey("/F"))
            {
                using var encoded = new MemoryStream();
                using (var compressor = new ZLibStream(encoded, CompressionLevel.SmallestSize, true))
                    for (var offset = 0; offset < stream.Value.Length; offset += 65536)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        compressor.Write(stream.Value, offset, Math.Min(65536, stream.Value.Length - offset));
                    }
                if (encoded.Length < stream.Value.Length) { stream.Value = encoded.ToArray(); dictionary.Elements.SetName("/Filter", "/FlateDecode"); }
            }
            progress?.Report(new(i + 1, objects.Length + 1, "Lossless stream compression"));
        }
        try
        {
            progress?.Report(new(objects.Length, objects.Length + 1, "Saving and validating copy"));
            var path = SafeOutput.Commit(output, policy, [input.Path], stream => { doc.Save(stream, false); size = stream.Length; }, temp =>
            {
                using var verify = new Opened(new(temp), PdfDocumentOpenMode.Import);
                if (verify.Document.PageCount != pageCount || verify.Encrypted || Forms(verify.Document) != forms)
                    throw new PdfOperationException("Output", "Compressed output validation failed.");
                if (size >= original) throw new PdfOperationException("NoReduction", "No size reduction was achieved; no output was saved.");
            }, cancellation);
            return new CompressionResult(path, original, size, "Success");
        }
        catch (PdfOperationException e) when (e.Code == "NoReduction") { return new(null, original, size, "No reduction; no output saved"); }
    }, cancellation);
}
