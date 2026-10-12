# PDF Toolbox

**PDF tools that stay on your device**

An independent ALMARFELD Windows desktop project. Open PDFs, review page choices and export a new copy, entirely on your computer. This is the first desktop development milestone, not a published release.

![Real Windows development build with synthetic pages](docs/screenshots/organize.png)

## Working tools

| Tool | Current behavior |
| --- | --- |
| Merge | Combine all pages of ordered input PDFs. |
| Split | Ranges, every page, every N pages, or one PDF per selected page. |
| Organize Pages | Lazy thumbnails, drag/button reordering, rotation, deletion, duplication and session undo. |
| Extract Pages | Selected ranges into one PDF or separate copies. |
| Rotate | Selected ranges or all pages; existing rotation is retained. |
| Images to PDF | JPG/JPEG, PNG and BMP; A4/Letter, landscape, margins, Fit/Fill and JPEG quality. Source image preview. |
| Metadata | Inspect document information; edit Title, Author, Subject, Keywords and Creator in a copy. Producer is library-controlled. |

PDF to Images, watermark, page numbering, Protect/Unlock, compression and general batch processing are explicitly disabled. WEBP is not supported in this milestone.

## Requirements and first run

Windows 10 **version 2004 or later**, or Windows 11, x64. The self-contained development build includes the .NET 8 runtime. It does **not** require WebView2. Windows supplies the PDF thumbnail renderer.

Extract the entire build folder and keep its adjacent files together. Run `PDFToolbox.exe`. There is no installer, automatic updater or automatic dependency download. The Windows binary is not Authenticode signed; Windows may show a reputation warning. Do not disable Windows security.

Use Open files / Ctrl+O, or drop supported files onto the window. Select a tool, review options and choose Export copy / Ctrl+S. Organize changes affect only the export plan. Recent files and passwords remain session-only; closing the application clears them.

## Safety and limits

- Originals are never overwritten, including when chosen as the output or through a detected hard-link alias. Replace is available only for a separate existing output after confirmation. Rename is the default collision policy.
- Output is staged beside the destination, reopened to validate page count, and finalized atomically. Failed/cancelled stages are discarded. Split is per-output: completed parts remain, and failed/skipped parts are reported individually.
- Password-protected PDFs can be inspected using a valid password. **All encrypted PDF editing is refused**, including owner-only encryption, until explicit Protect/Unlock support is implemented. Passwords are never saved or logged.
- Known signature dictionaries/fields and interactive forms are refused for editing. This is not cryptographic signature verification.
- Page imports do not promise preservation of bookmarks, document-level links, attachments, tags, accessibility structure or other advanced PDF features. Keep the original and verify output in your PDF reader before relying on it.
- Metadata editing is **not sanitization**: XMP, content and attachments may retain information.
- Limits: 256 MiB and 5,000 pages per PDF; 100 PDF inputs and 512 MiB combined per operation; 10,000 output pages. Engine images limit: 500; UI workspace limit: 100 files. Images are limited to 256 MiB and 40 megapixels each. Large/malformed files may still exceed available memory or be unsupported.
- Cancellation is checked between work units and before finalization; an individual library parse/render/save call may finish before cancellation takes effect.

## Offline and privacy

No accounts, uploads, ads, telemetry, analytics or network code. Files are processed locally. Thumbnails use a bounded, in-memory session cache; no preview files are persisted. Output temporary files are removed after failure/cancellation. See [privacy](docs/privacy.md) and [security](SECURITY.md).

## Build and tests

Install a .NET SDK supporting .NET 8 and Windows desktop/WinRT targeting (CI uses SDK 10 with the .NET 8 test runtime).

```powershell
dotnet restore PDFToolboxWindows.sln --locked-mode
dotnet build PDFToolboxWindows.sln -c Release --no-restore
dotnet test PDFToolboxWindows.sln -c Release --no-build
dotnet publish src/PDFToolbox.Windows -c Release --no-restore -o artifacts/windows/PDFToolbox
```

Do not publish development artifacts as a release without completing the release checks. The screenshot above is captured from the actual Windows application using synthetic input, with no cursor or personal paths. Architecture and validation details: [architecture](docs/architecture.md), [testing](docs/testing.md), [dependencies/licenses](docs/pdf-libraries.md).

MIT. [ALMARFELD](https://almarfeld.com/) · [Support](https://almarfeld.com/support/)
