# Security

Report vulnerabilities privately to security@almarfeld.com. Do not attach confidential PDFs, passwords or personal paths to a public issue. Provide a synthetic reproduction where possible.

The first desktop milestone processes files locally, with no network services. It is not a sandbox, malware scanner, signature validator, PDF sanitizer or document anonymizer. Treat unknown files cautiously. PDF libraries may reject valid advanced features or consume significant resources for malformed input.

Editing encrypted PDFs, detected signatures and interactive forms is refused. This does not certify a document as unsigned or safe. Page import can change document-level features. Always retain and compare the original. The binary is not Authenticode signed.

Only the current development branch is maintained; no public Windows release exists yet.
