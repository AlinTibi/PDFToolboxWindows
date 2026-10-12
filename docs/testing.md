# Testing

Run `dotnet test PDFToolboxWindows.sln -c Release` on Windows. Tests construct synthetic PDFs and images in uniquely-owned temporary directories; they do not read customer documents. Output files are reopened and their page dimensions, order, rotations, content streams and metadata are checked. Original hashes are compared.

Coverage includes ranges, merge, all split modes, organization/deletion/duplication, extraction, rotation, image import, metadata, encrypted/owner-only inputs, invalid passwords/files, empty inputs, Unicode/long paths, locked destinations, filename collisions, explicit replace, cancellation, partial split failures, staged-file cleanup and real Windows PDF thumbnail rendering/cache bounds.

The first milestone did not implement watermark, page numbers or PDF-to-images exports. The current milestone adds those tools and the regression coverage below. Tests do not establish PDF security certification, advanced document preservation or rendering equivalence for arbitrary PDFs.

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

## Rendering and overlay milestone — 2026-10-12

- **93 automated cases passed**, zero failed/skipped: the original 50 plus 43 new cases. Exported PDFs reopen; exported PNG/JPEG images are decoded, checked for dimensions/content and the selected DPI.
- New coverage: both raster formats, ranges/invalid ranges, mixed portrait/landscape/rotated geometry, source-page filenames, conflicts/approved replacements, locked output, cancellation/partial results and invalid options. Overlay tests render the actual outputs and compare changed-pixel regions for nine watermark positions, six number positions, transparency, rotation, selected pages, skips, crop boxes, starting offset/affixes and source hashes. Preview and export pixels match, and the shared cache is bounded at 16 variants.
- A 120-page mixed-geometry synthetic document was numbered, reopened and rendered to 120 decodable images. One local run measured **28 ms** to first preview and **1,385 ms** total, with **194 MiB** peak working set in the test process. A 20-page image-heavy PDF was watermarked/reopened and three pages exported to JPEG: **284 ms**, **220 MiB** peak including fixture creation/test host. These are local observations, not performance promises.
- The actual self-contained Windows build launched from a fresh folder with spaces, using a non-project working directory. Native Open/Save/folder selection and Cancel were exercised. GUI exports: four PNGs, four JPEGs, text watermark PDF, image watermark PDF and numbered PDFs with affixes/starting offset. Outputs were independently reopened/decoded; the original synthetic source hash remained unchanged.
- Ctrl+S opens the current tool's copy-export workflow; cancelling its native Save dialog produced no new file. Ctrl+O/native invalid-input handling and Tab navigation were checked. All new input controls and existing unnamed form controls have accessible names; file/page arrow buttons identify the selected item/action. Keyboard focus has a separate higher-contrast border in light mode.
- GUI cancellation during a 120-page **600-DPI** JPEG export retained **2 complete images** and reported **118 skipped**, with no staged temporary files. The long-running application process peaked at approximately **651 MiB**, then about **349 MiB** working set after cancellation. Rendering/encoding a large single page can allocate several buffers; bounded sequential processing is not a low-memory guarantee.
- Dark/light themes and physical window sizes **1366×768, 1920×1080 and 2560×1440** were checked at actual **150% OS scale**. A compact viewport equivalent to 1366×768 at 200% was also exercised by resizing, with scroll access to the controls. Results are collapsed initially and expandable; compact workspaces scroll vertically instead of permanently clipping commands.
- **Not independently tested:** actual OS scaling at **100%, 125% and 200%**, Windows 10, a full screen-reader audit and broad real-world PDF corpus. Resizing is not claimed as changing/testing those OS DPI settings. No global display/security/network settings were changed.
- Protect/Unlock, compression and general batch remain disabled. Encrypted editing/raster export is deliberately refused; overlays also refuse unsupported non-zero media origins/custom UserUnit and off-page layouts.

`docs/screenshots/watermark.png` is a real native client-area capture with synthetic content, no cursor, private paths or reconstructed UI. No release, website change or WinGet submission was made.
