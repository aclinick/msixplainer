# CLI Reference

The MSIXplainer CLI ships as `MSIXplainer.Cli`. Once published, run it as `msixplainer`; during development, prefix every command with `dotnet run --project MSIXplainer.Cli --`.

```text
msixplainer [path | --sample] [options]      # Manifest review
msixplainer diff <old> <new> [options]        # Update-size diff + bandwidth planner
msixplainer rules list                        # Print every rule + effective severity
```

## Exit codes

The same codes apply to every subcommand. They're designed for CI/CD gating.

| Code | Meaning |
|---|---|
| `0` | Clean — no warnings, no critical findings |
| `1` | One or more **Warning** findings |
| `2` | One or more **Critical** findings |

When multiple files are analyzed in one invocation (e.g. via glob), the **worst** exit code wins.

---

## `msixplainer` — analyze (default)

Analyzes one or more `.msix`, `.appx`, `.msixbundle`, or `.appxbundle` files and prints a categorized findings report.

```powershell
msixplainer path\to\package.msix
msixplainer "C:\packages\*.msix"
msixplainer --sample
```

### Options

| Flag | Description |
|---|---|
| `--sample` | Analyze the built-in Contoso Collaboration Hub sample manifest |
| `--severity <level>` | Hide findings below this severity. One of `Info`, `Review`, `Warning`, `Critical`. |
| `--markdown`, `--md` | Emit Markdown report instead of the terminal table |
| `--json` | Emit structured JSON (stable schema) |
| `--quiet`, `-q` | No output, exit code only — for CI gates |
| `--output <file>`, `-o <file>` | Write output to a file instead of stdout |
| `--rules <file>` | Load an extra rule-severity override file *on top of* `%LOCALAPPDATA%\MSIXplainer\rules.json` |
| `--help`, `-h` | Print usage |

### Examples

```powershell
# Export to Markdown
msixplainer package.msix --markdown -o review.md

# Export to JSON
msixplainer package.msix --json -o review.json

# CI gate: fail the build on any Warning or worse
msixplainer package.msix --quiet
if ($LASTEXITCODE -ne 0) { throw "Manifest review failed" }

# Override severities for a team-wide policy
msixplainer package.msix --rules .\team-rules.json
```

---

## `msixplainer diff` — update-size diff

Compares two packages and reports how much would actually download for an in-place update, using `AppxBlockMap.xml` block-hash diffing (byte-exact parity with Microsoft's `comparepackage.exe`).

```powershell
msixplainer diff old.msix new.msix
msixplainer diff old.msixbundle new.msixbundle
```

### Options

| Flag | Description |
|---|---|
| `--devices <N>` | Number of devices in the rollout — enables the bandwidth planner |
| `--link <list>` | Comma-separated link speeds in Mbps (e.g. `100,1000`). Defaults to `100,1000` when planner enabled. |
| `--cost <usd>` | Egress cost per GB in USD |
| `--top <N>` | Show top N changed files (default: 25) |
| `--markdown`, `--md` | Emit Markdown report |
| `--json` | Emit JSON |
| `--output <file>`, `-o <file>` | Write to file |
| `--quiet`, `-q` | No output, exit code only |
| `--help`, `-h` | Print usage |

### Examples

```powershell
# Basic: how big is the update?
msixplainer diff app-1.0.msix app-1.1.msix

# Fleet rollout estimate
msixplainer diff app-1.0.msix app-1.1.msix `
  --devices 5000 --link 100,1000 --cost 0.08

# Export the diff
msixplainer diff app-1.0.msix app-1.1.msix --markdown -o update.md
msixplainer diff app-1.0.msix app-1.1.msix --json -o update.json
```

See [`update-diff.md`](update-diff.md) for the math.

---

## `msixplainer rules list`

Prints every rule ID the engine knows about, its default severity, and the effective severity after applying overrides from `%LOCALAPPDATA%\MSIXplainer\rules.json` (and any `--rules` file).

```powershell
msixplainer rules list
```

Useful for discovering the exact rule IDs to put in your own `rules.json`. See [`rules.md`](rules.md) for the full catalog.
