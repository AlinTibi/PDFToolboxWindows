# Architecture

The .NET 8 Windows WPF shell uses native dialogs, keyboard shortcuts, background operations, cancellation and session view models. `PDFToolbox.Windows` owns UI/workspace state; `PdfEngine` owns models, ranges, validation, operations, output transactions and thumbnails. Engine tests do not require the application window.

`PdfService` imports pages through pinned PDFsharp-WPF. `SafeOutput` stages an exclusively-created temporary file on the destination volume, validates it and uses atomic move/replace. Input streams deny concurrent writes during each operation. Output paths matching inputs, detected hard links and reparse-point destinations are refused. Split reports success/failure/skipped per output; it is deliberately not an all-or-nothing transaction across multiple files.

`ThumbnailService` renders visible pages using Windows.Data.Pdf. A semaphore bounds concurrent rendering to one; at most 16 immutable bitmaps are retained per session. List virtualization avoids rendering every page. The engine never creates a permanent thumbnail cache. Image previews decode to a bounded display size on a worker task; export decodes one image at a time.

Metadata, export plans and passwords are session memory only. There is no settings database, upload service or network client. File dialogs are the shell boundary. Future tool implementations can reuse cancellation, progress and SafeOutput; unfinished tools are not wired to misleading buttons.

No guarantee is made for preservation of advanced document-level PDF features. Encrypted inputs retain a parsed security handler after the library resets its output encryption mode; that handler is used to refuse unintended decryption. This behavior is pinned and regression-tested.
