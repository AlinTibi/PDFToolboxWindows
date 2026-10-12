# Privacy

PDF Toolbox processes PDF and image files locally. It has no account, telemetry, analytics, ads, upload endpoint or automatic download code. It does not call external services to inspect a file or resolve a password.

Recent paths, document information, passwords and previews exist only in process memory for the session. Overlay previews use one modified PDF page in memory and are not written to disk. Temporary output files are created only beside a requested destination and removed if that output fails or is cancelled. Successfully completed split parts and rendered images remain and are reported.

Exported PDFs may contain metadata, embedded content or other information carried by the source or library. Metadata editing does not promise removal of all personal information. Operating-system dialogs and memory management remain subject to Windows behavior; the application cannot guarantee secure memory erasure or prevent OS crash dumps/page files.

Protect and Unlock use passwords only in process memory, never in reports, recent-file entries, configuration or logging. Their controls are cleared after export/cancel and on tool changes. An encrypted source opened for preview retains its credential until removed/closed. There is no password recovery, cracking, external verification or cloud processing. Lossless compression reports sizes and paths, not credentials, and does not sanitize metadata or attachments.
