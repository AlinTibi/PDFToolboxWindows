# Contributing

Open an issue with a synthetic reproduction before a substantial change. Keep PDF operations independent of the UI. Add tests that reopen outputs and verify contents/page order and unchanged sources; a success return value alone is insufficient.

Run the README build/test commands on Windows. New dependencies require a documented license review. Avoid GPL/AGPL, non-commercial-only or paid-runtime dependencies. No telemetry, automatic downloads or cloud processing.

Submit a focused pull request. Never commit confidential files, credentials, absolute personal paths or generated build artifacts. Tests must use synthetic data. Preserve the distinction between metadata editing and sanitization, and between encryption and signature validation.
