# Privacy

PDF Toolbox processes PDF and image files locally. It has no account, telemetry, analytics, ads, upload endpoint or automatic download code. It does not call external services to inspect a file or resolve a password.

Recent paths, document information, passwords and previews exist only in process memory for the session. Overlay previews use one modified PDF page in memory and are not written to disk. Temporary output files are created only beside a requested destination and removed if that output fails or is cancelled. Successfully completed split parts and rendered images remain and are reported.

Exported PDFs may contain metadata, embedded content or other information carried by the source or library. Metadata editing does not promise removal of all personal information. Operating-system dialogs and memory management remain subject to Windows behavior; the application cannot guarantee secure memory erasure or prevent OS crash dumps/page files.
