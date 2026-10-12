# PDF dependencies and licenses

Reviewed before implementation. The Windows edition does not reuse Android advertising or analytics dependencies.

| Component | Purpose | License / distribution |
| --- | --- | --- |
| PDFsharp-WPF 6.2.4 (empira) | Read/import, merge, page selection/reordering/rotation, images and document information | MIT. Preserve its copyright/license in THIRD-PARTY-NOTICES.txt. No paid runtime required. |
| Windows.Data.Pdf | On-demand raster thumbnails | Windows operating-system API, supplied by supported Windows installations. Not an application-bundled PDF engine; use is subject to the user's Windows license. |
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

PDFsharp is not a full preservation/sanitization engine. Rewritten PDFs may lose document-level features; digitally signed PDFs are refused by the MVP to avoid invalidating signatures silently. Metadata editing is not secure sanitization and does not guarantee anonymity. Reliable compression has not been validated and remains disabled.
