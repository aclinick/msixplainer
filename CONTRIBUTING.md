# Contributing to MSIXplainer

Thanks for your interest in contributing! This guide will help you get started.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Windows App SDK 2.0](https://learn.microsoft.com/windows/apps/windows-app-sdk/) (for WinUI app only)
- Windows 10 1809+ or Windows 11

## Getting Started

```bash
git clone https://github.com/aclinick/msixexplainer.git
cd msixexplainer
```

### Build

```bash
# Core library and CLI
dotnet build MSIXplainer.Core
dotnet build MSIXplainer.Cli

# WinUI app (requires Windows App SDK)
dotnet build MSIXplainer
```

### Test

```bash
dotnet test MSIXplainer.Core.Tests
```

### Run

```bash
# Analyze a package
dotnet run --project MSIXplainer.Cli -- path/to/app.msix

# Analyze a bundle
dotnet run --project MSIXplainer.Cli -- path/to/app.msixbundle

# Use the built-in sample manifest
dotnet run --project MSIXplainer.Cli -- --sample
```

## Project Structure

| Project | Description |
|---------|-------------|
| `MSIXplainer.Core` | Parsing, rules engine, export — all static classes, no DI |
| `MSIXplainer.Cli` | Spectre.Console CLI with tree output, Markdown/JSON export |
| `MSIXplainer` | WinUI 3 desktop app with CommunityToolkit.Mvvm |
| `MSIXplainer.Core.Tests` | xUnit test suite |

## How to Contribute

1. **Fork** the repository
2. **Create a branch** from `main` (`git checkout -b feature/my-feature`)
3. **Make your changes** — follow the existing code style
4. **Add tests** for any new functionality
5. **Run the test suite** and ensure all tests pass
6. **Commit** with a descriptive message (e.g., `feat: add new rule for X capability`)
7. **Open a Pull Request** against `main`

## Adding a New Rule

The rules engine lives in `MSIXplainer.Core/Services/RulesEngine.cs`. Each rule is a static method that:

1. Examines the `XDocument` manifest
2. Returns a `Finding` with severity, title, description, and remediation
3. Is called from `Analyze()` which collects and sorts all findings

Add a corresponding test in `MSIXplainer.Core.Tests/RulesEngineTests.cs`.

## Code Style

- All Core services are **static classes** — no dependency injection
- Use **C# latest** language features (primary constructors, collection expressions, etc.)
- Keep methods focused and testable
- Comment only when clarification is needed

## Reporting Issues

Please use the [issue templates](https://github.com/aclinick/msixexplainer/issues/new/choose) for bug reports and feature requests.
