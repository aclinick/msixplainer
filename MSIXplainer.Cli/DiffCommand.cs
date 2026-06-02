using System.Text;
using MSIXplainer.Models;
using MSIXplainer.Services;
using Spectre.Console;

namespace MSIXplainer;

/// <summary>
/// Handles the `msixplainer diff &lt;old&gt; &lt;new&gt;` subcommand: compares two MSIX
/// packages or bundles and prints/exports update download size + bandwidth plan.
/// </summary>
static class DiffCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var options = ParseArgs(args);
        if (options is null) return 1;

        if (!File.Exists(options.OldPath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Old package not found: {Markup.Escape(options.OldPath)}");
            return 1;
        }
        if (!File.Exists(options.NewPath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] New package not found: {Markup.Escape(options.NewPath)}");
            return 1;
        }

        UpdateDiffResult result;
        try
        {
            var oldIsBundle = ManifestParserService.IsBundleFile(options.OldPath);
            var newIsBundle = ManifestParserService.IsBundleFile(options.NewPath);

            if (oldIsBundle != newIsBundle)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Both inputs must be the same kind — either two .msix/.appx files, or two .msixbundle/.appxbundle files.");
                return 1;
            }

            result = oldIsBundle
                ? UpdateDiffService.CompareBundles(options.OldPath, options.NewPath)
                : UpdateDiffService.ComparePackages(options.OldPath, options.NewPath);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        BandwidthEstimate? bandwidth = null;
        // Bandwidth — use total wire bytes (delta + metadata) for accuracy.
        if (options.DeviceCount is { } devices)
        {
            try
            {
                bandwidth = BandwidthPlannerService.Calculate(
                    deltaBytesPerDevice: result.TotalUpdateDownloadBytes,
                    deviceCount: devices,
                    linkSpeedsMbps: options.LinkSpeedsMbps,
                    costPerGigabyteUsd: options.CostPerGigabyteUsd);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Bandwidth planner: {Markup.Escape(ex.Message)}");
                return 1;
            }
        }

        switch (options.Format)
        {
            case OutputFormat.Json:
                Write(options, DiffExportService.ExportToJson(result, bandwidth));
                break;
            case OutputFormat.Markdown:
                Write(options, DiffExportService.ExportToMarkdown(result, bandwidth, options.TopFiles));
                break;
            case OutputFormat.Quiet:
                // No output, just exit code.
                break;
            default:
                PrintConsoleReport(result, bandwidth, options.TopFiles);
                break;
        }

        return 0;
    }

    static void Write(DiffOptions options, string content)
    {
        if (!string.IsNullOrEmpty(options.OutputFile))
        {
            File.WriteAllText(options.OutputFile, content);
            if (options.Format != OutputFormat.Quiet)
                AnsiConsole.MarkupLine($"[green]Wrote[/] {Markup.Escape(options.OutputFile)}");
        }
        else
        {
            Console.Write(content);
        }
    }

    static void PrintConsoleReport(UpdateDiffResult result, BandwidthEstimate? bandwidth, int topFiles)
    {
        AnsiConsole.Write(new Rule($"[bold]Update Impact[/]").LeftJustified());
        AnsiConsole.MarkupLine($"[grey]Old:[/] {Markup.Escape(result.OldLabel)}");
        AnsiConsole.MarkupLine($"[grey]New:[/] {Markup.Escape(result.NewLabel)}");
        AnsiConsole.WriteLine();

        // Headline
        var headline = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("")
            .AddColumn(new TableColumn("Bytes").RightAligned())
            .AddColumn(new TableColumn("Human").RightAligned());

        headline.AddRow(
            "[bold]Fresh install (full)[/]",
            $"{result.TotalFullDownloadBytes:N0}",
            $"[cyan]{DiffExportService.Human(result.TotalFullDownloadBytes)}[/]");
        headline.AddRow(
            "[bold]Update impact (block delta)[/]",
            $"{result.TotalDeltaDownloadBytes:N0}",
            $"[green]{DiffExportService.Human(result.TotalDeltaDownloadBytes)}[/]");
        headline.AddRow(
            "[grey]+ Metadata overhead[/]",
            $"{result.TotalOverheadBytes:N0}",
            $"[grey]{DiffExportService.Human(result.TotalOverheadBytes)}[/]");
        headline.AddRow(
            "[bold]= Total wire bytes per device[/]",
            $"{result.TotalUpdateDownloadBytes:N0}",
            $"[green bold]{DiffExportService.Human(result.TotalUpdateDownloadBytes)}[/]");
        headline.AddRow(
            "[bold]Savings vs. full install[/]",
            "—",
            $"[bold green]{result.SavingsPercent:F1}%[/]");
        AnsiConsole.Write(headline);

        // File inventory / disk footprint summary
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]File inventory & disk footprint[/]").LeftJustified());
        var totalAdded = result.PackageDiffs.Sum(p => p.AddedFilesUncompressedBytes);
        var totalRemoved = result.PackageDiffs.Sum(p => p.RemovedFilesUncompressedBytes);
        var totalChangedNet = result.PackageDiffs.Sum(p => p.ChangedFilesNetSizeBytes);
        var totalUnchanged = result.PackageDiffs.Sum(p => p.UnchangedFilesUncompressedBytes);
        var totalNewSize = result.PackageDiffs.Sum(p => p.NewPackageUncompressedBytes);
        var addedCount = result.PackageDiffs.Sum(p => p.AddedFileCount);
        var removedCount = result.PackageDiffs.Sum(p => p.RemovedFileCount);
        var modifiedCount = result.PackageDiffs.Sum(p => p.ModifiedFileCount);
        var unchangedCount = result.PackageDiffs.Sum(p => p.UnchangedFileCount);

        var inv = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn(new TableColumn("Count").RightAligned())
            .AddColumn(new TableColumn("Bytes").RightAligned())
            .AddColumn(new TableColumn("Human").RightAligned());
        inv.AddRow("[green]Added files[/]", $"{addedCount:N0}", $"{totalAdded:N0}", DiffExportService.Human(totalAdded));
        inv.AddRow("[red]Deleted files[/]", $"{removedCount:N0}", $"{totalRemoved:N0}", DiffExportService.Human(totalRemoved));
        inv.AddRow("[yellow]Modified files (net size shift)[/]", $"{modifiedCount:N0}", $"{totalChangedNet:N0}", DiffExportService.SignedHuman(totalChangedNet));
        inv.AddRow("[grey]Unchanged files[/]", $"{unchangedCount:N0}", $"{totalUnchanged:N0}", DiffExportService.Human(totalUnchanged));
        inv.AddRow("[bold]New package total[/]", "—", $"{totalNewSize:N0}", $"[cyan]{DiffExportService.Human(totalNewSize)}[/]");
        inv.AddRow("[bold]Additional disk space needed[/]", "—",
            $"{result.TotalInstalledSizeDifferenceBytes:N0}",
            $"[bold {(result.TotalInstalledSizeDifferenceBytes >= 0 ? "yellow" : "green")}]{DiffExportService.SignedHuman(result.TotalInstalledSizeDifferenceBytes)}[/]");
        AnsiConsole.Write(inv);

        // Warnings
        if (result.Warnings.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow bold]⚠ Warnings[/]");
            foreach (var w in result.Warnings)
                AnsiConsole.MarkupLine($"  • {Markup.Escape(w)}");
        }

        if (result.AddedPackages.Count > 0)
        {
            AnsiConsole.MarkupLine($"[cyan]Added inner packages:[/] {Markup.Escape(string.Join(", ", result.AddedPackages))}");
        }
        if (result.RemovedPackages.Count > 0)
        {
            AnsiConsole.MarkupLine($"[cyan]Removed inner packages:[/] {Markup.Escape(string.Join(", ", result.RemovedPackages))}");
        }

        // Per-package
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Per-package breakdown[/]").LeftJustified());
        var pkgTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Package")
            .AddColumn("Old → New")
            .AddColumn(new TableColumn("Full").RightAligned())
            .AddColumn(new TableColumn("Delta").RightAligned())
            .AddColumn(new TableColumn("Overhead").RightAligned())
            .AddColumn(new TableColumn("Total wire").RightAligned())
            .AddColumn(new TableColumn("Savings").RightAligned())
            .AddColumn(new TableColumn("Blocks").RightAligned());

        foreach (var p in result.PackageDiffs)
        {
            pkgTable.AddRow(
                Markup.Escape(p.Label),
                $"{Markup.Escape(p.OldVersion)} → {Markup.Escape(p.NewVersion)}",
                DiffExportService.Human(p.FullDownloadBytes),
                DiffExportService.Human(p.DeltaDownloadBytes),
                DiffExportService.Human(p.OverheadBytes),
                DiffExportService.Human(p.TotalUpdateDownloadBytes),
                $"{p.SavingsPercent:F1}%",
                $"{p.ReusedBlocks}/{p.TotalBlocks}");
        }
        AnsiConsole.Write(pkgTable);

        // Duplicate-file optimization opportunities
        var allDuplicates = result.PackageDiffs
            .SelectMany(p => p.DuplicateGroups.Select(g => (Package: p.Label, Group: g)))
            .ToList();

        if (allDuplicates.Count > 0)
        {
            AnsiConsole.WriteLine();
            var totalReclaim = allDuplicates.Sum(d => d.Group.PossibleSizeReductionBytes);
            AnsiConsole.Write(new Rule($"[bold]Optimization: {allDuplicates.Count} duplicate file groups — reclaim {DiffExportService.Human(totalReclaim)}[/]").LeftJustified());
            var dupTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Package")
                .AddColumn(new TableColumn("Copies").RightAligned())
                .AddColumn(new TableColumn("Per copy").RightAligned())
                .AddColumn(new TableColumn("Reclaim").RightAligned())
                .AddColumn("Example path");

            foreach (var (pkg, g) in allDuplicates
                .OrderByDescending(x => x.Group.PossibleSizeReductionBytes)
                .Take(15))
            {
                dupTable.AddRow(
                    Markup.Escape(pkg),
                    g.CopyCount.ToString(),
                    DiffExportService.Human(g.PerCopyUncompressedBytes),
                    DiffExportService.Human(g.PossibleSizeReductionBytes),
                    Markup.Escape(Truncate(g.Paths[0], 60)));
            }
            AnsiConsole.Write(dupTable);
        }

        // Top files
        var topFileChanges = result.PackageDiffs
            .SelectMany(p => p.Files.Select(f => (Package: p.Label, File: f)))
            .Where(x => x.File.DeltaBytes > 0 || x.File.Status == FileDiffStatus.Removed
                        || x.File.Status == FileDiffStatus.Added)
            .OrderByDescending(x => x.File.DeltaBytes)
            .Take(topFiles)
            .ToList();

        if (topFileChanges.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]Top {topFileChanges.Count} files by delta[/]").LeftJustified());
            var fileTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Package")
                .AddColumn("File")
                .AddColumn("Status")
                .AddColumn(new TableColumn("Delta").RightAligned())
                .AddColumn(new TableColumn("New").RightAligned())
                .AddColumn(new TableColumn("Old").RightAligned())
                .AddColumn(new TableColumn("Blocks").RightAligned());

            foreach (var (pkg, file) in topFileChanges)
            {
                fileTable.AddRow(
                    Markup.Escape(pkg),
                    Markup.Escape(Truncate(file.Path, 50)),
                    StatusTag(file.Status),
                    DiffExportService.Human(file.DeltaBytes),
                    DiffExportService.Human(file.NewSize),
                    DiffExportService.Human(file.OldSize),
                    $"{file.ReusedBlocks}/{file.TotalBlocks}");
            }
            AnsiConsole.Write(fileTable);
        }

        // Bandwidth
        if (bandwidth is not null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold]Bandwidth & cost projection[/]").LeftJustified());
            AnsiConsole.MarkupLine($"  [grey]Delta per device:[/] {DiffExportService.Human(bandwidth.DeltaBytesPerDevice)} ({bandwidth.DeltaBytesPerDevice:N0} bytes)");
            AnsiConsole.MarkupLine($"  [grey]Devices:[/] {bandwidth.DeviceCount:N0}");
            AnsiConsole.MarkupLine($"  [grey]Total transfer:[/] [bold]{DiffExportService.Human(bandwidth.TotalBytes)}[/] ({bandwidth.TotalBytes:N0} bytes)");
            if (bandwidth.EstimatedCostUsd is { } cost)
                AnsiConsole.MarkupLine($"  [grey]Estimated egress cost:[/] [bold]${cost:N2} USD[/] (at ${bandwidth.CostPerGigabyteUsd:N3}/GB)");

            var bwTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("Link").RightAligned())
                .AddColumn("Per device")
                .AddColumn("Serial fleet");

            foreach (var p in bandwidth.LinkProjections)
            {
                bwTable.AddRow(
                    $"{p.LinkSpeedMbps:N0} Mbps",
                    DiffExportService.FormatDuration(p.PerDeviceDuration),
                    DiffExportService.FormatDuration(p.SerialFleetDuration));
            }
            AnsiConsole.Write(bwTable);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey italic]Delta is estimated from AppxBlockMap.xml hashes — mirrors how App Installer / Store / MDM updates download only changed blocks.[/]");
    }

    static string StatusTag(FileDiffStatus s) => s switch
    {
        FileDiffStatus.Added => "[green]+ added[/]",
        FileDiffStatus.Removed => "[red]- removed[/]",
        FileDiffStatus.Modified => "[yellow]~ modified[/]",
        _ => "[grey]= unchanged[/]"
    };

    static string Truncate(string s, int max) =>
        s.Length <= max ? s : "…" + s[^(max - 1)..];

    static DiffOptions? ParseArgs(string[] args)
    {
        var opts = new DiffOptions();
        var positional = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--json":
                    opts.Format = OutputFormat.Json;
                    break;
                case "--markdown":
                case "--md":
                    opts.Format = OutputFormat.Markdown;
                    break;
                case "--quiet":
                case "-q":
                    opts.Format = OutputFormat.Quiet;
                    break;
                case "--output":
                case "-o":
                    if (++i >= args.Length) return Fail("--output requires a value");
                    opts.OutputFile = args[i];
                    break;
                case "--devices":
                    if (++i >= args.Length) return Fail("--devices requires a value");
                    if (!int.TryParse(args[i], out var devs) || devs < 1)
                        return Fail($"--devices must be a positive integer (got '{args[i]}')");
                    opts.DeviceCount = devs;
                    break;
                case "--link":
                case "--links":
                    if (++i >= args.Length) return Fail("--link requires a value");
                    opts.LinkSpeedsMbps = ParseLinks(args[i]) ?? [];
                    if (opts.LinkSpeedsMbps.Count == 0) return Fail($"--link could not parse '{args[i]}' as Mbps values");
                    break;
                case "--cost":
                    if (++i >= args.Length) return Fail("--cost requires a value");
                    if (!double.TryParse(args[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cost) || cost < 0)
                        return Fail($"--cost must be a non-negative number (got '{args[i]}')");
                    opts.CostPerGigabyteUsd = cost;
                    break;
                case "--top":
                    if (++i >= args.Length) return Fail("--top requires a value");
                    if (!int.TryParse(args[i], out var top) || top < 0)
                        return Fail($"--top must be a non-negative integer (got '{args[i]}')");
                    opts.TopFiles = top;
                    break;
                default:
                    if (a.StartsWith('-'))
                        return Fail($"Unknown option '{a}'. Use --help for usage.");
                    positional.Add(a);
                    break;
            }
        }

        if (positional.Count != 2)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] `diff` requires exactly two paths: <old> <new>");
            PrintHelp();
            return null;
        }

        opts.OldPath = positional[0];
        opts.NewPath = positional[1];

        // Default link speeds when planner is enabled but no --link supplied.
        if (opts.DeviceCount is not null && opts.LinkSpeedsMbps.Count == 0)
            opts.LinkSpeedsMbps = [100, 1000];

        return opts;
    }

    static DiffOptions? Fail(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        return null;
    }

    static List<int>? ParseLinks(string raw)
    {
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var mbps) || mbps <= 0) return null;
            list.Add(mbps);
        }
        return list;
    }

    static void PrintHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Usage: msixplainer diff <old> <new> [options]");
        sb.AppendLine();
        sb.AppendLine("Compares two MSIX packages or bundles and reports how much would");
        sb.AppendLine("be downloaded for an update, using AppxBlockMap block-hash diffing.");
        sb.AppendLine();
        sb.AppendLine("Options:");
        sb.AppendLine("  --devices N         Devices in the rollout (enables bandwidth planner)");
        sb.AppendLine("  --link 100,1000     Link speeds in Mbps, comma separated (default 100,1000)");
        sb.AppendLine("  --cost 0.08         Egress cost per GB in USD");
        sb.AppendLine("  --top N             Show top N changed files (default 25)");
        sb.AppendLine("  --markdown, --md    Markdown output");
        sb.AppendLine("  --json              JSON output");
        sb.AppendLine("  --output, -o <f>    Write output to a file");
        sb.AppendLine("  --quiet, -q         No output, exit code only");
        sb.AppendLine("  --help, -h          Show this help");
        Console.Write(sb.ToString());
    }
}

sealed class DiffOptions
{
    public string OldPath { get; set; } = "";
    public string NewPath { get; set; } = "";
    public OutputFormat Format { get; set; } = OutputFormat.Console;
    public string? OutputFile { get; set; }
    public int? DeviceCount { get; set; }
    public List<int> LinkSpeedsMbps { get; set; } = [];
    public double? CostPerGigabyteUsd { get; set; }
    public int TopFiles { get; set; } = 25;
}
