# PDF Toolbox v1.1.0

- Adds local PDF-to-Excel/CSV table extraction from selectable text, page-range selection and table previews.
- Exports one XLSX worksheet per table with text cells, preserving leading zeros and long identifiers. CSV supports optional apostrophe protection for formula-like cells; spreadsheet import can still reinterpret values.
- Reports pages without sufficient selectable text; OCR is not included. Layout inference requires review. Consecutive-page joining is opt-in and retains repeated headers.
- Reuses source identity checks, staged writes and separate-copy export. Existing table outputs are renamed; successful CSV parts remain reported if a later part fails or is cancelled.
- Adds regression coverage for extraction, text preservation, OOXML structure, CSV escaping, failure cleanup and cancellation.
- Updates the transitive PKCS cryptography dependency to the patched 8.0.2 release.

Windows 10 version 2004 or later / Windows 11 x64. Self-contained portable ZIP; no separate .NET runtime or WebView2 is required. Offline processing, no accounts, uploads, telemetry or automatic downloads. Executable is not Authenticode signed. Existing PDF feature-preservation limitations remain; this is not OCR or direct PDF text editing.

## Previous release: v1.0.0

PDF Toolbox is a local Windows x64 utility, distributed as a self-contained portable ZIP. Extract the entire package and run PDFToolbox.exe; no separate .NET runtime or WebView2 installation is required.

- Merge, split, organize/reorder/duplicate/delete/undo, extract and rotate PDF pages.
- Convert supported images to PDF and PDF pages to PNG/JPEG.
- Edit document information in a copy; add text/image watermarks and page numbers.
- Protect a copy with AES-128/PDF 1.6; Unlock with valid credentials (owner credentials for restricted documents).
- Lossless PDF stream compression reports actual byte counts and saves no result when it is not smaller. Already optimized or scanned PDFs may not shrink.

Processing stays on the user's device, without accounts, uploads, telemetry, ads, automatic updates or downloads. Sources are not overwritten. Keep originals and inspect exported copies; advanced PDF preservation is not universally guaranteed. Signed PDFs are not rewritten. OCR, general batch processing and direct PDF text editing are unavailable.

Target: Windows 10 version 2004 or later / Windows 11 x64. Windows 11 was tested; Windows 10 has not been independently tested. The executable is not Authenticode signed; Windows may show a reputation/security warning. Obtain it only from the official GitHub release and optionally verify the published SHA-256. Do not disable Windows security.
