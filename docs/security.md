# Security Model

MSIXplainer treats every MSIX/AppX/bundle file as **untrusted input**. The whole point of the tool is to inspect packages you don't trust yet — so the tool itself has to be hardened against malicious packages.

## What MSIXplainer will never do

- **Execute any code from a package.** Period. Not the install scripts, not registered COM servers, not anything. The package is opened as a ZIP archive and only XML is read.
- **Touch the Windows package store.** No `Add-AppxPackage`, no `PackageManager` calls.
- **Make network requests.** Zero telemetry, zero analytics, zero LLM calls, zero update checks. See [`PRIVACY.md`](../PRIVACY.md).
- **Require elevation.** Runs entirely with standard-user permissions.

## Zip-bomb and entity-expansion guards

The ZIP and XML parsers are configured defensively:

| Guard | Limit | Why |
|---|---|---|
| `AppxManifest.xml` size cap | 10 MB | Real manifests are <100 KB. Anything bigger is hostile. |
| Icon extraction cap | 1 MB | Prevents giant inlined images from blowing memory. |
| `XmlReaderSettings.DtdProcessing` | `Prohibit` | Blocks DTD-based XXE attacks. |
| `XmlReaderSettings.XmlResolver` | `null` | Cannot resolve external entities (no SSRF via XML). |
| `XmlReaderSettings.MaxCharactersFromEntities` | `0` | Defeats billion-laughs / entity-expansion bombs. |
| `XmlReaderSettings.MaxCharactersInDocument` | 10 MB | Hard cap on parsed XML size. |

A malicious package trying any of these tricks fails to parse with a clear error — it never destabilizes the host process.

## ZIP traversal

The tool only reads named entries (`AppxManifest.xml`, `AppxBlockMap.xml`, etc.) by their exact name. There is no path-based file extraction step, so Zip-Slip / `..\` traversal isn't reachable.

## What the tool *does* read

| Entry | Purpose | Behavior on hostile input |
|---|---|---|
| `AppxManifest.xml` | Manifest analysis | Fails safe — guards above kick in, or schema mismatch surfaces as a parse error |
| `AppxBlockMap.xml` | Update-diff block hashes | Same XML guards. Malformed → diff aborts cleanly. |
| Icon assets (during analysis only) | Display in the WinUI app | 1 MB cap; on read error, falls back to placeholder |

The WinUI app displays icons read from the package, but they're rendered through the WinUI `Image` control just like any local file — no native image decoders are exposed to attacker-controlled bytes beyond what the OS itself uses.

## Reporting a security issue

Please don't open a public issue for vulnerabilities. See [`SECURITY.md`](../SECURITY.md) in the repo root for the responsible disclosure process.
