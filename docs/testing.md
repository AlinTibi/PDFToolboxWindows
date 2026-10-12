# Testing

Run `dotnet test PDFToolboxWindows.sln -c Release` on Windows. Tests construct synthetic PDFs and images in uniquely-owned temporary directories; they do not read customer documents. Output files are reopened and their page dimensions, order, rotations, content streams and metadata are checked. Original hashes are compared.

Coverage includes ranges, merge, all split modes, organization/deletion/duplication, extraction, rotation, image import, metadata, encrypted/owner-only inputs, invalid passwords/files, empty inputs, Unicode/long paths, locked destinations, filename collisions, explicit replace, cancellation, partial split failures, staged-file cleanup and real Windows PDF thumbnail rendering/cache bounds.

Watermark, page numbers and PDF-to-images user exports are disabled and are not claimed as tested. Tests do not establish PDF security certification, advanced document preservation or rendering equivalence for arbitrary PDFs.

Windows GUI validation must use the actual self-contained build from a fresh folder with spaces: Open/Save/Cancel, export/reopen, source hash, empty/error states, organization/undo, native drag/drop, resizing, keyboard navigation, taskbar/Alt+Tab icon and practical display scales. Any untested scale/environment must be reported rather than inferred from an automated engine test.

## First milestone validation — 2026-10-12

- 50 automated cases passed, zero failures and zero skipped. Windows PDF rendering/cache tests run against the real operating-system API.
- Release-configuration build and self-contained x64 publish passed with zero warnings/errors. No public release was created.
- Actual packaged build launched from a fresh folder containing spaces, with a non-project working directory. Native Open/Save and cancelling Save were exercised.
- Native Explorer drag/drop added a second synthetic PDF to an existing workspace. Page drag moved order from 1,2,3,4 to 2,3,1,4; Undo restored 1,2,3,4. Thumbnail recycling was verified after reordering/undo.
- GUI exports reopened: merge (8 pages), organization/duplication (5 pages), reorder (4 pages in 2,1,3,4 order), rotation (90 degrees) and metadata. The source SHA-256 remained identical. Engine tests additionally cover all split modes, extraction and supported image formats/Fit/Fill.
- Invalid input produced a factual PDF-header error without changing the existing sources. Ctrl+O worked; Open/Save Cancel and visible keyboard focus were checked. Ctrl+S requires a final independent shortcut check; a prior automation attempt was inconclusive.
- The distinct icon was visually checked in the window, taskbar and Alt+Tab. Dark/light views and resizing were checked at the current **150%** Windows scale, including 980×680 minimum, 1024×768 and 1320×900 logical window sizes. Smaller panes scroll rather than hide operations.
- **Not yet independently tested:** Windows 10 machine; actual OS scaling at 100%, 125% and 200%; exhaustive keyboard/screen-reader audit; broad real-world PDF compatibility; long-running large-file performance profiling. No global display, firewall or security settings were changed to simulate these checks.

The real screenshot in `docs/screenshots/organize.png` is a crop of the native application client area, using synthetic documents. It contains no reconstructed UI, cursor or personal filesystem paths.
