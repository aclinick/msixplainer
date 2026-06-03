# MSIXplainer

A Windows tool that turns MSIX/AppX package manifests into plain-English IT security reviews. Available as a **WinUI 3 desktop app** and a **CLI tool**.

Instead of reading raw XML, you get categorized findings with severity ratings, explanations of what each manifest entry does, why the app might need it, and what an IT Pro should care about.

![License](https://img.shields.io/github/license/aclinick/msixplainer)
[![CI](https://github.com/aclinick/msixplainer/actions/workflows/ci.yml/badge.svg)](https://github.com/aclinick/msixplainer/actions/workflows/ci.yml)

---

## What It Does

- **Opens `.msix` or `.appx` files** and extracts the manifest safely (package is treated as untrusted input — no code is executed)
- **Analyzes 18 security-relevant categories**: trust level, restricted capabilities, standard capabilities, device access, network access, virtualization bypasses, startup tasks, protocol handlers, app URI handlers, file associations, COM registrations, background tasks, Office integration, WebView2, VDI indicators, Windows services, allowElevation bypass, and identity validation
- **Explains every manifest section** in plain English with severity tags (`🔴 CRITICAL`, `🟡 WARNING`, `🔵 REVIEW`, `ℹ️ INFO`)
- **Exports** annotated Markdown reports (section-by-section walkthrough with XML snippets and explanation tables) and structured JSON
- **Uses a local deterministic rules engine** — no cloud service, no LLM dependency

## Screenshots

### CLI Output

```
$ msixplainer contoso-hub.msix

╭──────────────────────────────────────────────╮
│  MSIXplainer                     │
╰──────────────────────────────────────────────╯

┌────────────────────────────────────────────────┐
│ Package Identity                               │
├──────────────┬─────────────────────────────────┤
│ Name         │ Contoso.CollaborationHub        │
│ Version      │ 24.10.1.100                     │
│ Architecture │ x64                             │
│ Publisher    │ Contoso Ltd                     │
│ Min OS       │ 10.0.19041.0                    │
└──────────────┴─────────────────────────────────┘

  Risk: 2 critical · 6 warning · 8 review · 3 info

  Findings
  ├── 🔴 Critical
  │   ├── Runs with Full Trust
  │   └── Restricted capability: broadFileSystemAccess
  ├── 🟡 Warning
  │   ├── Filesystem virtualization DISABLED
  │   ├── Registry virtualization DISABLED
  │   └── ...
  └── 🔵 Review
      ├── Protocol handler: contoso-hub://
      └── ...
```

### CLI Markdown Export Example

```powershell
$ msixplainer contoso-hub.msix --markdown --output review.md
```

---

## Getting Started

### Prerequisites

- Windows 10 version 2004 (build 19041) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview)
- Windows App SDK 2.0+ (for the WinUI app)

### Build

```powershell
# Clone
git clone https://github.com/aclinick/msixplainer.git
cd msixplainer

# Build everything
dotnet build

# Or build individual projects
dotnet build MSIXplainer.Core
dotnet build MSIXplainer.Cli
dotnet build MSIXplainer
```

### Run the CLI

```powershell
# Analyze a real package
dotnet run --project MSIXplainer.Cli -- path\to\package.msix

# Use the built-in sample manifest (Contoso Collaboration Hub)
dotnet run --project MSIXplainer.Cli -- --sample

# Export to Markdown
dotnet run --project MSIXplainer.Cli -- --sample --markdown --output review.md

# Export to JSON
dotnet run --project MSIXplainer.Cli -- --sample --json

# Filter by severity
dotnet run --project MSIXplainer.Cli -- package.msix --severity warning

# Quiet mode (exit code only — useful for CI)
dotnet run --project MSIXplainer.Cli -- package.msix --quiet

# Analyze multiple packages with glob
dotnet run --project MSIXplainer.Cli -- "C:\packages\*.msix"
```

#### CLI Exit Codes

| Code | Meaning |
|------|---------|
| `0`  | No warnings or critical findings |
| `1`  | Warnings found |
| `2`  | Critical findings found |

These exit codes make the CLI usable as a CI/CD gate.

#### Customizing Rule Severities

Every rule emitted by the engine has a stable `RuleId` (e.g. `trust.fullTrust`,
`virt.filesystemDisabled`, `services.windowsService`). You can override the
severity of any rule without changing the rule text by dropping a JSON file at:

```
%LOCALAPPDATA%\MSIXplainer\rules.json
```

Both the CLI and the WinUI app auto-load this file on every analysis. The CLI
also accepts `--rules <file>` to layer an additional override file on top —
useful for checking team-wide rules.json into a repo for CI gating.

Example `rules.json`:

```json
{
  "trust.fullTrust": "Info",
  "services.windowsService": "Warning",
  "capability.broadFileSystemAccess": "Critical"
}
```

Valid severities: `Info`, `Review`, `Warning`, `Critical`. Unknown rule IDs and
unrecognized severity values are skipped with a warning.

To see every available rule ID, its default, and the effective severity after
overrides, run:

```powershell
msixplainer rules list
```

Rule text (Title, Description, WhyItMatters, Recommendation) is intentionally
**not** user-editable — only the severity dial is.

### Run the WinUI App

```powershell
# From the MSIXplainer directory
cd MSIXplainer
.\BuildAndRun.ps1
```

Or open the solution in Visual Studio and run the `MSIXplainer` project.

---

## Project Structure

```
msixplainer/
├── MSIXplainer.Core/                # Shared class library (no UI deps)
│   ├── Models/                      # ManifestFinding, PackageInfo, BlockMapEntry,
│   │                                  BundleInnerPackage, UpdateDiffResult, etc.
│   └── Services/
│       ├── ManifestParserService.cs       # Safe ZIP/XML extraction
│       ├── BundleManifestParser.cs        # .msixbundle / .appxbundle support
│       ├── BlockMapParser.cs              # AppxBlockMap.xml parser
│       ├── RulesEngine.cs                 # 18-rule analysis engine
│       ├── RuleCatalog.cs / RuleSeverityOverrides.cs  # Severity tuning
│       ├── ManifestExplainerService.cs    # Section-by-section explainer
│       ├── ExportService.cs               # Manifest review export (MD + JSON)
│       ├── UpdateDiffService.cs           # SDK-parity update size analysis
│       ├── DiffExportService.cs           # Update diff export (MD + JSON)
│       ├── BandwidthPlannerService.cs     # Fleet rollout estimator
│       └── SampleManifest.cs              # Built-in test manifest
├── MSIXplainer/                     # WinUI 3 desktop app (packaged MSIX)
│   ├── Pages/                       # MainPage, ComparePage, RulesPage
│   ├── ViewModels/                  # MVVM with CommunityToolkit.Mvvm
│   └── Package.appxmanifest
└── MSIXplainer.Cli/                 # Spectre.Console CLI
    └── Program.cs                   # analyze + diff subcommands
```

## Analysis Categories

| Category | What It Checks |
|----------|---------------|
| Identity | Package name, publisher certificate, version |
| Trust Level | Full trust vs. AppContainer sandboxing |
| Restricted Capabilities | `broadFileSystemAccess`, `appDiagnostics`, `runFullTrust`, etc. |
| Standard Capabilities | Internet, removable storage, documents library, etc. |
| Device Access | Microphone, webcam, location, Bluetooth |
| Network Access | Internet, private network, server capabilities |
| Virtualization | Filesystem and registry virtualization bypasses |
| Startup | Auto-start tasks registered at user login |
| Protocols | Custom URI scheme handlers (e.g., `app-name://`) |
| App URI Handlers | Web domain interception |
| File Associations | File type registrations |
| Background Tasks | Push notifications, timers, system event handlers |
| COM Registration | Out-of-process COM servers (Office add-ins, shell extensions) |
| Office Integration | Outlook/Office indicators |
| WebView2 | Embedded browser dependencies |
| VDI | Virtual desktop infrastructure indicators |
| Services | Windows service registrations |
| Elevation | `allowElevation` package extension bypasses |

## Security Model

The tool treats every package as **untrusted input**:

- **No code execution** — packages are opened as ZIP archives, only the manifest XML is read
- **Safe XML parsing** — DTD processing is prohibited, XML resolver is null, entity expansion is capped
- **ZIP bomb guard** — manifest entries larger than 10 MB are rejected; icon extraction capped at 1 MB
- **No elevation** — the tool runs with standard user permissions

## Markdown Export

The Markdown export produces an annotated document similar to a professional security review:

- Section-by-section manifest walkthrough with numbered headings
- XML code blocks for each manifest section
- Explanation tables with severity tags and recommendations
- "How to Read This Document" guide
- Risk assessment callout
- Findings summary table with all findings ranked by severity

## License

[MIT](LICENSE)

