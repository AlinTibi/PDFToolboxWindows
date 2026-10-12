# Architecture

The .NET 8 Windows WPF shell uses native dialogs, keyboard shortcuts, background operations, cancellation and session view models. `PDFToolbox.Windows` owns UI/workspace state; `PdfEngine` owns models, ranges, validation, operations, output transactions and thumbnails. Engine tests do not require the application window.

`PdfService` imports pages through pinned PDFsharp-WPF. `SafeOutput` stages an exclusively-created temporary file on the destination volume, validates it and uses atomic move/replace. Input streams deny concurrent writes during each operation. Output paths matching inputs, detected hard links and reparse-point destinations are refused. Split reports success/failure/skipped per output; it is deliberately not an all-or-nothing transaction across multiple files.

`ThumbnailService` renders visible pages using Windows.Data.Pdf. A semaphore bounds concurrent rendering to one; at most 16 immutable bitmaps are retained per session. List virtualization avoids rendering every page. The engine never creates a permanent thumbnail cache. Image previews decode to a bounded display size on a worker task; export decodes one image at a time.

`PdfRendering` is the common renderer for thumbnails, overlay previews and page-image export. A preview variant includes input session identity, file version, page, width and immutable overlay settings; image versions invalidate stale variants. Switching/typing cancels previous work and debounces a new preview. Recycled/unloaded page rows release their bitmap references, so scrolling does not accumulate every thumbnail outside the bounded cache.

`OverlayService` uses the same drawing method for export and an in-memory single-page PDF preview. Displayed crop-box coordinates are mapped back through /Rotate into original page coordinates. Existing page size/rotation/content are preserved, and content is appended rather than flattening the document. A preview stores no temporary PDF on disk. PDFsharp calls are bounded/cancellable between pages, not interrupted inside a save call.

Image export loads the source renderer once, validates page dimensions, renders and encodes one selected page at a time, decodes the staged image, then uses SafeOutput. Each result is honest on partial failure/cancellation; replacement is limited to targets explicitly approved by the caller. Raster outputs include the chosen pixel dimensions/DPI regardless of display scale. Large single-page encoding can still allocate several buffers; sequential export does not imply constant low memory.

Metadata, export plans and passwords are session memory only. There is no settings database, upload service or network client. File dialogs are the shell boundary. Future tool implementations can reuse cancellation, progress and SafeOutput; unfinished tools are not wired to misleading buttons.

No guarantee is made for preservation of advanced document-level PDF features. Encrypted inputs retain a parsed security handler after the library resets its output encryption mode; that handler is used to refuse unintended decryption. This behavior is pinned and regression-tested.
