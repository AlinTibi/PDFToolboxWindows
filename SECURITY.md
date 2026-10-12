# Security

Report vulnerabilities privately to security@almarfeld.com. Do not attach confidential PDFs, passwords or personal paths to a public issue. Provide a synthetic reproduction where possible.

PDF Toolbox processes files locally, with no network services. It is not a sandbox, malware scanner, signature validator, PDF sanitizer or document anonymizer. Treat unknown files cautiously. PDF libraries may reject valid advanced features or consume significant resources for malformed input.

Detected signatures are refused for rewriting; this is not cryptographic signature verification. Encrypted PDFs require explicit password-based Unlock before other editing/export tools can operate on a copy. Restricted documents require owner credentials. Page-editing tools refuse interactive forms; security operations and lossless compression retain the existing document tree, subject to the documented library limitations. Page import can change document-level features. Always retain and compare the original. The binary is not Authenticode signed.

Security fixes target the current 1.0.x product line. Protect uses AES-128/PDF 1.6, not identity authentication; passwords are never logged or persisted by the application. Neither metadata editing nor compression is secure sanitization. Immutable .NET strings and Windows memory management do not guarantee physical password erasure.
