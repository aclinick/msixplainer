# Copilot Instructions — MSIX Manifest Explainer

## Build & Run

```powershell
# Build everything
dotnet build

# Run CLI with built-in sample manifest
dotnet run --project MsixExplainer.Cli -- --sample

# Run CLI against a real package
dotnet run --project MsixExplainer.Cli -- path\to\package.msix

# Run WinUI app (requires Developer Mode)
cd MsixExplorer && .\BuildAndRun.ps1
```

There are no tests in this repository.

## Architecture

Three-project .NET 10 solution (`MsixExplainer.slnx`):

- **MsixExplainer.Core** — Shared class library with all analysis logic. No UI dependencies.
- **MsixExplainer.Cli** — Console frontend using Spectre.Console. Hand-rolled arg parsing in `Program.cs`.
- **MsixExplorer** — WinUI 3 desktop app (Windows App SDK 2.0, packaged MSIX).

### Core processing pipeline

All analysis flows through the same Core services in this order:

1. **`ManifestParserService`** — Extracts `AppxManifest.xml` from `.msix`/`.appx` ZIP archives with security guards (DTD prohibited, XML resolver null, 10 MB cap, no code execution).
2. **`RulesEngine.Analyze(XDocument)`** — Static `Analyze` method runs 18 deterministic rule methods against manifest XML, returns `List<ManifestFinding>` sorted by severity then category. Each finding has `Category`, `Severity`, `Title`, `Description`, `WhyItMatters`, `Recommendation`, and optional `XmlSnippet`.
3. **`ManifestExplainerService`** — Builds section-by-section `ManifestSection` + `ManifestPropertyGroup` structures with plain-English explanations of every XML element.
4. **`ExportService`** — Generates annotated Markdown reports and structured JSON from findings.

### Adding a new analysis rule

Add a private `Analyze*` method in `RulesEngine.cs` and call it from `Analyze()`. Each rule method receives `(XElement root, List<ManifestFinding> findings)` and appends findings. Use `FindingSeverity` (Critical/Warning/Review/Info) and `FindingCategory` enum values. If adding a new category, update `FindingCategory` and `ManifestFinding.CategoryLabel` in the Models, and add a section entry in `ManifestExplainerService.BuildSections`.

## Workflow

- **Phased implementation** — Break work into discrete phases. Complete and verify each phase before moving to the next.
- **Test every change** — All code changes must have corresponding tests. Run tests to confirm they pass before committing.
- **Commit when green** — Once tests pass, commit to GitHub. Do not leave passing work uncommitted.

## Key Conventions

- **Namespace:** All projects use `RootNamespace` of `MsixExplorer` (note: the solution folder is `msixexplainer`, project folders use mixed casing like `MsixExplainer.Core`, but the C# namespace is always `MsixExplorer`).
- **All Core services are static classes** — no DI, no interfaces. The CLI and WinUI app call them directly.
- **Models use `required init` properties** — `ManifestFinding`, `ManifestSection` use C# `required` + `init` pattern.
- **WinUI app uses CommunityToolkit.Mvvm** — `MainPageViewModel` uses `[ObservableProperty]` source generators with the partial property syntax (`public partial bool IsPackageLoaded { get; set; }`).
- **Security model** — Packages are always treated as untrusted input. Never execute code from packages. XML parsing must prohibit DTD processing and null out the XML resolver.
- **Severity levels:** Critical (`🔴`), Warning (`🟡`), Review (`🔵`), Info (`ℹ️`) — used consistently in CLI output, Markdown export, and WinUI display.
- **CLI exit codes:** 0 = clean, 1 = warnings found, 2 = critical findings found. These support CI/CD gating.
