# PDF dependencies and licenses

Reviewed before implementation. The Windows edition does not reuse Android advertising or analytics dependencies.

| Component | Purpose | License / distribution |
| --- | --- | --- |
| PDFsharp-WPF 6.2.4 (empira) | Read/import, page operations, image import, metadata and appended watermark/number content | MIT. Preserve its copyright/license in THIRD-PARTY-NOTICES.txt. No paid runtime required. |
| Windows.Data.Pdf | On-demand previews and PNG/JPEG page export | Windows operating-system API, supplied by supported Windows installations. Not an application-bundled PDF engine; use is subject to the user's Windows license. |
| .NET 8 and WPF | Application/runtime and image decoding | MIT for the open-source runtime/WPF implementations; distributed runtime notices accompany the self-contained development build. |

Primary references:

- [PDFsharp license](https://github.com/empira/PDFsharp/blob/master/LICENSE)
- [Pinned WPF package](https://www.nuget.org/packages/PDFsharp-WPF/6.2.4)
- [Windows PDF API](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfdocument)
- [Calling WinRT from desktop .NET](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
- [.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- [WPF license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)

Runtime package license metadata was checked after restore:

| Restored component | Version | License |
| --- | --- | --- |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT |
| System.Security.Cryptography.Pkcs | 8.0.1 | MIT |
| Microsoft.NETCore.App.Runtime.win-x64 | 8.0.31 in local build | MIT, with upstream third-party notices |
| Microsoft.WindowsDesktop.App.Runtime.win-x64 | 8.0.31 in local build | MIT, with upstream third-party notices |
| WinRT.Runtime / CsWinRT | 2.2.0.48161 assembly | MIT |
| Microsoft.Windows.SDK.NET.Ref projection assemblies | 10.0.19041.57 | Microsoft Windows SDK license; redistributable projection assemblies, not a paid PDF runtime. Full terms: Windows-SDK-License.rtf. |

CI may select a later serviced .NET 8 runtime; its notices must accompany any future distributed build. Full package/runtime notices are preserved in THIRD-PARTY-NOTICES.txt. Development tests use xUnit (Apache-2.0) and Microsoft.NET.Test.Sdk (MIT); those tools are not distributed with the application. The icon generation helper uses Pillow (HPND), development-only.

No GPL, AGPL, non-commercial-only or paid-runtime PDF dependency is used. No PDFium, Ghostscript, iText or commercial SDK is bundled. The dependency vulnerability audit currently reports no vulnerable packages.

The rendering/overlay milestone adds **no NuGet dependencies**. WPF's PNG/JPEG codecs encode images and image-watermark alpha; Windows fonts are accessed locally through the existing WPF PDFsharp package. No font package or PDF rasterizer is downloaded. Windows render-option dimensions are DIPs; output is normalized to requested pixel dimensions and assigned the selected DPI. Reference: [DestinationWidth](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpagerenderoptions.destinationwidth).

PDFsharp is not a full preservation/sanitization engine. Rewritten PDFs may lose document-level features; digitally signed PDFs are refused to avoid invalidating signatures silently. Metadata editing is not secure sanitization and does not guarantee anonymity.

The security/compression milestone adds **no packages**. PDFsharp-WPF remains pinned to **6.2.4 (MIT)**. Protect explicitly calls `SetEncryptionToV4UsingAES`: PDF 1.6, security-handler V=4/R=4, AESV2 with a 128-bit key, verified from the saved security dictionary and password/content round trips. Only this mode is exposed. PDFsharp Modify-mode reading enforces owner credentials; Unlock resets encryption/password state only after successful reading. References: [PDFsharp encryption source](https://github.com/empira/PDFsharp/blob/v6.2.4/src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Security/PdfStandardSecurityHandler.cs), [reader credential enforcement](https://github.com/empira/PDFsharp/blob/v6.2.4/src/foundation/src/PDFsharp/src/PdfSharp/Pdf.IO/PdfReader.cs).

Compression uses .NET 8 `ZLibStream` (runtime MIT) for eligible unfiltered streams, plus existing PDFsharp content compression. Existing filters and encoded images are retained. There is no image downsampling/recompression, external compressor, new PDF runtime or paid dependency. A smaller size is not guaranteed; larger trials are not committed. Synthetic size/content/rotation/metadata/form/annotation checks are documented in testing.md.
