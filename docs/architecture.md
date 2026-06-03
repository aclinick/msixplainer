# Architecture

A high-level map of the codebase for contributors. For *what the tool does*, see the [root README](../README.md).

## Three-project solution

```
msixplainer/
├── MSIXplainer.Core/         # Shared class library — no UI deps, no DI
├── MSIXplainer.Cli/          # Spectre.Console terminal frontend
└── MSIXplainer/              # WinUI 3 desktop app (packaged MSIX)
```

All three target **.NET 10**. The Core project is `AnyCPU;ARM64;x64`; the CLI and WinUI app build per-platform.

## Why this split

- **Core has zero UI dependencies.** It can be referenced from anything — a future Avalonia app, a PowerShell module, a unit-test runner, an Azure Function — without dragging WinUI in.
- **CLI and WinUI both call Core directly.** They share the same parsing, rules engine, explainer, and export code. A bug fixed in Core is fixed everywhere.
- **No DI container, no interfaces in Core.** Services are static classes. Frontends call them directly. This keeps Core dependency-free and trivially testable.

## Processing pipeline

Every analysis — whether triggered from the CLI, the WinUI app, or a test — flows through the same Core services in this order:

```
.msix / .msixbundle file
        │
        ▼
ManifestParserService          ← safe ZIP + XML extraction (see security.md)
        │
        ▼
RulesEngine.Analyze            ← 18+ rule methods → List<ManifestFinding>
        │                        (severities can be tuned by RuleSeverityOverrides)
        ▼
ManifestExplainerService       ← section-by-section ManifestSection tree
        │
        ▼
ExportService                  ← Markdown / JSON output
```

For the update-diff path, the pipeline forks at parsing:

```
old.msix + new.msix
        │
        ▼
BlockMapParser                 ← AppxBlockMap.xml → List<BlockMapEntry>
        │
        ▼
UpdateDiffService              ← block-hash diff (byte-exact comparepackage.exe parity)
        │
        ▼
BandwidthPlannerService        ← per-link/per-fleet projections (pure math)
        │
        ▼
DiffExportService              ← Markdown / JSON output
```

Bundles (`.msixbundle`, `.appxbundle`) go through `BundleManifestParser` first, which unpacks the outer ZIP and recurses into each inner package.

## Models

Models live in `MSIXplainer.Core/Models/`. Conventions:

- **`required init` properties** — `ManifestFinding`, `ManifestSection`, etc. use C# `required` + `init`.
- **Records or sealed classes** — never inheritance hierarchies. Use composition.
- **Categories and severities are enums** — `FindingCategory`, `FindingSeverity`. Adding a new category requires updating `ManifestFinding.CategoryLabel` and `ManifestExplainerService.BuildSections`.

## Adding a new analysis rule

1. Add a private `Analyze*` method in `RulesEngine.cs`.
2. Call it from `RulesEngine.Analyze()`.
3. Emit findings with a stable `RuleId` (e.g. `trust.someThing`).
4. **Add a matching entry to `RuleCatalog.All`** in `RuleCatalog.cs`. The `EveryRuleEmittedBySample_HasCatalogEntry` test enforces this — your build will go red if you forget.
5. If introducing a new `FindingCategory`, update the enum + `ManifestFinding.CategoryLabel` + `ManifestExplainerService.BuildSections`.
6. Add a unit test in `MSIXplainer.Core.Tests/RulesEngineTests.cs`.

The rule **text** (Title, Description, WhyItMatters, Recommendation) is compiled in — users can only tune **severity** via `rules.json`, not the wording. This is intentional: it keeps explanations consistent across teams using the same rule.

## WinUI app structure

```
MSIXplainer/
├── App.xaml(.cs)
├── MainWindow.xaml(.cs)         # Hosts the navigation frame
├── MainPage.xaml(.cs)           # Single-file manifest review
├── Pages/
│   ├── ComparePage.xaml(.cs)    # Update-diff + bandwidth planner UI
│   └── ...
├── ViewModels/
│   ├── MainPageViewModel.cs
│   └── ComparePageViewModel.cs
└── Package.appxmanifest         # Store identity — see CONTRIBUTING.md before changing
```

- **MVVM via [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)** — `[ObservableProperty]` source generators on `partial` properties.
- **No code-behind logic.** Pages bind to ViewModels; ViewModels call Core services. The only code-behind is event wiring that XAML can't express.
- **No DI container.** ViewModels `new` their dependencies, which are all static Core services anyway.

## Testing

- **`MSIXplainer.Core.Tests`** is xUnit. 118+ tests covering parsing, rules engine, diff, bandwidth planner, exports.
- **Run all:** `dotnet test MSIXplainer.Core.Tests`
- **Single test:** `dotnet test MSIXplainer.Core.Tests --filter "FullyQualifiedName~TestName"`
- **CI** runs on every push and PR via `.github/workflows/ci.yml`. PRs are gated on this passing.
- **No WinUI UI tests yet** — manual smoke via `Package.ps1` + sideload.

## Packaging

- **`Package.ps1`** at the repo root builds x64 + ARM64 release packages, signs each, and bundles them into a `.msixbundle` in `artifacts/`.
- The bundle is signed with `devcert.pfx` (gitignored, auto-generated on first run via `winapp cert generate`).
- For Store releases, the upload bundle is the signed `.msixbundle`; Partner Center re-signs with the Store certificate during publishing.

See [`PRIVACY.md`](../PRIVACY.md) for the privacy posture and [`security.md`](security.md) for the threat model.
