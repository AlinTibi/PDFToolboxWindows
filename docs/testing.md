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

## Security, lossless compression and memory milestone — 2026-10-12

- **118 automated cases passed**, zero failed/skipped (93 previous + 25 new). Protect/Unlock verify AESV2, V=4/R=4, correct/wrong/missing credentials, owner restriction enforcement, confirmation/empty/non-ASCII validation, decoded page content, geometry/forms, encryption state and source hashes. Compression tests cover actual byte counts, decoded page streams, title, rotation, annotation counts/forms, refusal of encrypted input, no-reduction output suppression, cancellation during work, locked/existing output, Unicode/long paths and simulated write failure cleanup. Preview tests exercise 120 pages, cache eviction, estimated 24 MiB/16-entry bounds, ten reopen/clear cycles and cancellation without cached output.
- Synthetic compression matrix (one observed local run; byte counts can vary slightly with PDF timestamps). These are test results, not expected savings for other documents:

| Input | Original bytes | Trial/output bytes | Saved | Result |
| --- | ---: | ---: | ---: | --- |
| Text-heavy, 3 pages | 57,990 | 40,648 | 29.91% | Saved/reopened; decoded page content retained |
| Scanned/image-only, 3 pages | 4,142 | 4,910 | -18.54% | No output saved; encoded images already compact |
| Mixed text/images, 3 pages | 58,666 | 41,300 | 29.60% | Saved/reopened |
| Already optimized, 3 pages | 39,921 | 40,640 | -1.80% | No output saved |
| Text-heavy, 120 pages | 874,961 | 153,162 | 82.49% | Saved/reopened; 120 pages retained |
| Landscape/rotated, 3 pages | 58,020 | 40,674 | 29.90% | Saved/reopened; rotations/content retained |
| Metadata/form/annotations, 3 pages | 58,145 | 40,802 | 29.83% | Saved/reopened; title/form/annotations retained |
| Encrypted input | — | — | — | Refused; unlock with valid owner credentials first |

- Memory investigation identified the converted bitmap + managed RGBA + second bitmap and cached full decode for staged validation as unnecessary page-local copies. In the same isolated 12-page A4 JPEG stress fixture, old peak working set was **342 MiB at 300 DPI / 944 MiB at 600 DPI**; direct encoding/header validation reduced it to **172 MiB / 276 MiB**. Export dimensions, DPI and JPEG quality remain unchanged. The final combined test process exported **120 full A4 pages at 600 DPI**, about **87.4 s**, with **349 MiB cumulative peak working set / 428 MiB private memory at completion**. These process measurements include test-host overhead and GC behavior; no universal memory guarantee is made.
- Real Windows 11 GUI: launched a self-contained build from a fresh spaced folder with a non-project working directory. Protect created a password-protected copy through native Save As; that copy opened with the correct password. Unlock with the wrong password refused and produced no output; correct credentials produced a copy which reopened without a password. Independent reading/rendering verified page count/content. GUI compression measured **30,088 → 29,273 bytes**, **815 bytes / 2.71%** saved. Save As cancellation left no new outputs. Dark/light tool screens were inspected; password controls received matching theme/focus styling.
- GUI 120-page 600-DPI JPEG cancellation retained **41 complete decoded 600-DPI images** and reported **79 skipped**, with zero staged files left. The process peaked at **337 MiB**, then **261 MiB working set** after cancellation, compared with the previous milestone's 651 MiB observation. These GUI sessions/moments of cancellation differ and are not a controlled comparative benchmark. Original synthetic input SHA-256 remained `254BE7718F3FB3EEA3AC64CC50AED691530F2590D14524AB973DE5FE5E944042`.
- **Real OS scaling**, changed through Windows Display Settings and independently checked with `GetDpiForWindow`: **100% → 96 DPI**, **125% → 120 DPI**, **200% → 192 DPI**. The loaded workspace/thumbnail/export UI remained usable; the 12 raster cases also passed at 200%. The original **150% / 144 DPI** was restored. These are actual scaling checks, not equivalent window resizing; the entire workflow was not repeated at every scaling level.
- **Not independently verified:** Windows 10 (no Windows 10 VM available; the installed Sandbox uses the host Windows 11 family), full screen-reader audit, exhaustive advanced-form/annotation real-world corpus, real disk-full/access-denied fault injection, and new Explorer-to-app drag/drop in this milestone. Existing drag/drop code is unchanged; previous coverage is retained. General batch/OCR/PDF text editing/image downsampling remain unavailable. No website, tag, release or WinGet changes were made.
