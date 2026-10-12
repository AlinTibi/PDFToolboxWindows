# Testing

Run `dotnet test PDFToolboxWindows.sln -c Release` on Windows. Tests construct synthetic PDFs and images in uniquely-owned temporary directories; they do not read customer documents. Output files are reopened and their page dimensions, order, rotations, content streams and metadata are checked. Original hashes are compared.

Coverage includes ranges, merge, all split modes, organization/deletion/duplication, extraction, rotation, image import, metadata, encrypted/owner-only inputs, invalid passwords/files, empty inputs, Unicode/long paths, locked destinations, filename collisions, explicit replace, cancellation, partial split failures, staged-file cleanup and real Windows PDF thumbnail rendering/cache bounds.

Watermark, page numbers and PDF-to-images user exports are disabled and are not claimed as tested. Tests do not establish PDF security certification, advanced document preservation or rendering equivalence for arbitrary PDFs.

Windows GUI validation must use the actual self-contained build from a fresh folder with spaces: Open/Save/Cancel, export/reopen, source hash, empty/error states, organization/undo, native drag/drop, resizing, keyboard navigation, taskbar/Alt+Tab icon and practical display scales. Any untested scale/environment must be reported rather than inferred from an automated engine test.
