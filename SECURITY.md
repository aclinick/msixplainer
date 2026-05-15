# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest on `main` | ✅ |

## Reporting a Vulnerability

If you discover a security vulnerability in MSIXplainer, please report it responsibly:

1. **Do not** open a public issue
2. Email **msixplainer@users.noreply.github.com** with:
   - A description of the vulnerability
   - Steps to reproduce
   - Potential impact
3. You will receive an acknowledgement within **48 hours**
4. A fix will be developed and released as soon as possible

## Security Considerations

MSIXplainer is a **read-only analysis tool** — it never executes, installs, or modifies packages. However, because it parses untrusted XML from arbitrary MSIX packages, the following safeguards are in place:

- **DTD processing is prohibited** — prevents XML External Entity (XXE) attacks
- **XmlResolver is set to null** — no external resource loading
- **10 MB size cap** on extracted manifests — prevents zip-bomb denial of service
- **No code execution** from manifest content — all analysis is static

## Scope

This policy covers the MSIXplainer source code and published releases. Third-party dependencies are managed via NuGet and should be reported to their respective maintainers.
