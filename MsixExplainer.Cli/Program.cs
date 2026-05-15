using System.Text;
using MsixExplorer.Models;
using MsixExplorer.Services;
using Spectre.Console;

namespace MsixExplorer;

static class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        var options = ParseArgs(args);

        if (!options.IsValid)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Provide a path to an .msix/.appx file, or use --sample.");
            return 1;
        }

        // Collect all file paths (supports wildcards)
        var files = ResolveFiles(options);
        if (files.Count == 0 && !options.UseSample)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No matching files found.");
            return 1;
        }

        int worstExit = 0;

        if (options.UseSample)
        {
            worstExit = AnalyzeOne(null, options);
        }

        foreach (var file in files)
        {
            if (files.Count > 1 || options.UseSample)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(Path.GetFileName(file))}[/]").LeftJustified());
            }

            var exit = AnalyzeOne(file, options);
            if (exit > worstExit) worstExit = exit;
        }

        return worstExit;
    }

    static int AnalyzeOne(string? filePath, CliOptions options)
    {
        PackageInfo info;
        List<ManifestFinding> findings;
        System.Xml.Linq.XElement manifestRoot;

        try
        {
            var result = AnsiConsole.Status()
                .AutoRefresh(true)
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start("Analyzing...", ctx =>
                {
                    ctx.Status("Opening package...");
                    System.Xml.Linq.XDocument doc;
                    string rawXml;
                    PackageInfo pkgInfo;

                    if (filePath is null)
                    {
                        ctx.Status("Loading sample manifest...");
                        (doc, rawXml, pkgInfo) = ManifestParserService.ParseRawXml(SampleManifest.GetTeamsLikeManifest());
                    }
                    else
                    {
                        ctx.Status($"Extracting manifest from {Path.GetFileName(filePath)}...");
                        (doc, rawXml, pkgInfo) = ManifestParserService.ExtractFromPackage(filePath);
                    }

                    ctx.Status("Running rules engine...");
                    Thread.Sleep(80);
                    var f = RulesEngine.Analyze(doc);

                    ctx.Status("Generating report...");
                    Thread.Sleep(80);

                    pkgInfo.CriticalCount = f.Count(x => x.Severity == FindingSeverity.Critical);
                    pkgInfo.WarningCount = f.Count(x => x.Severity == FindingSeverity.Warning);
                    pkgInfo.ReviewCount = f.Count(x => x.Severity == FindingSeverity.Review);
                    pkgInfo.InfoCount = f.Count(x => x.Severity == FindingSeverity.Info);

                    return (pkgInfo, f, doc.Root!);
                });

            info = result.pkgInfo;
            findings = result.f;
            manifestRoot = result.Item3;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        // Apply severity filter
        if (options.MinSeverity is not null)
        {
            findings = findings
                .Where(f => f.Severity >= options.MinSeverity.Value)
                .ToList();
        }

        // Output
        switch (options.Format)
        {
            case OutputFormat.Json:
                Write(options, ExportService.ExportToJson(info, findings));
                break;
            case OutputFormat.Markdown:
                Write(options, ExportService.ExportToMarkdown(manifestRoot, info, findings));
                break;
            case OutputFormat.Quiet:
                // No output — just exit code
                break;
            default:
                PrintConsoleReport(info, findings);
                break;
        }

        if (info.CriticalCount > 0) return 2;
        if (info.WarningCount > 0) return 1;
        return 0;
    }

    static void Write(CliOptions options, string content)
    {
        if (options.OutputFile is not null)
        {
            File.WriteAllText(options.OutputFile, content, Encoding.UTF8);
            AnsiConsole.MarkupLine($"[green]✓[/] Written to [link]{Markup.Escape(options.OutputFile)}[/]");
        }
        else
        {
            Console.WriteLine(content);
        }
    }

    static void PrintConsoleReport(PackageInfo info, List<ManifestFinding> findings)
    {
        AnsiConsole.WriteLine();

        // Package identity table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold]MSIXplainer[/]")
            .AddColumn(new TableColumn("[grey]Property[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Value[/]"));

        table.AddRow("Package", Markup.Escape(info.Name));
        table.AddRow("Display Name", $"[bold]{Markup.Escape(info.DisplayName)}[/]");
        table.AddRow("Version", Markup.Escape(info.Version));
        table.AddRow("Publisher", Markup.Escape(info.PublisherLine));
        table.AddRow("Architecture", Markup.Escape(info.Architecture));
        table.AddRow("Min OS", Markup.Escape(info.MinOsVersion));
        table.AddRow("Frameworks", Markup.Escape(info.FrameworkDependencies));

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Summary bar
        var summaryTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("")
            .AddColumn("")
            .AddColumn("")
            .AddColumn("");

        summaryTable.AddRow(
            FormatCount(info.CriticalCount, "critical", "red"),
            FormatCount(info.WarningCount, "warning", "yellow"),
            FormatCount(info.ReviewCount, "review", "cyan"),
            FormatCount(info.InfoCount, "info", "grey")
        );

        AnsiConsole.Write(new Panel(summaryTable)
            .Header("[bold]Findings Summary[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey));

        AnsiConsole.WriteLine();

        if (findings.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No findings to report.[/]");
            return;
        }

        // Findings tree grouped by severity
        var tree = new Tree("[bold]Findings[/]");

        var grouped = findings
            .GroupBy(f => f.Severity)
            .OrderByDescending(g => g.Key);

        foreach (var group in grouped)
        {
            var (label, color) = group.Key switch
            {
                FindingSeverity.Critical => ("CRITICAL", "red"),
                FindingSeverity.Warning => ("WARNING", "yellow"),
                FindingSeverity.Review => ("REVIEW", "cyan"),
                _ => ("INFO", "grey")
            };

            var severityNode = tree.AddNode($"[{color} bold]{label}[/] [grey]({group.Count()})[/]");

            foreach (var finding in group)
            {
                var findingNode = severityNode.AddNode(
                    $"[{color}]●[/] [bold]{Markup.Escape(finding.Title)}[/] [grey]— {Markup.Escape(finding.CategoryLabel)}[/]");

                findingNode.AddNode($"[grey]What:[/] {Markup.Escape(finding.Description)}");
                findingNode.AddNode($"[yellow]Why:[/]  {Markup.Escape(finding.WhyItMatters)}");
                findingNode.AddNode($"[green]Fix:[/]  {Markup.Escape(finding.Recommendation)}");

                if (!string.IsNullOrWhiteSpace(finding.XmlSnippet))
                {
                    // Truncate long snippets for CLI readability
                    var snippet = finding.XmlSnippet.Length > 300
                        ? finding.XmlSnippet[..300] + "..."
                        : finding.XmlSnippet;
                    findingNode.AddNode(new Panel(Markup.Escape(snippet))
                        .Header("[grey]XML[/]")
                        .Border(BoxBorder.Rounded)
                        .BorderColor(Color.Grey));
                }
            }
        }

        AnsiConsole.Write(tree);
        AnsiConsole.WriteLine();

        // Footer
        AnsiConsole.Write(new Rule($"[grey]Generated by MSIXplainer — {DateTime.Now:yyyy-MM-dd HH:mm}[/]")
            .RuleStyle(Style.Parse("grey")).LeftJustified());
        AnsiConsole.WriteLine();
    }

    static string FormatCount(int count, string label, string color)
    {
        return count > 0
            ? $"[{color} bold]{count}[/] [{color}]{label}[/]"
            : $"[grey]{count} {label}[/]";
    }

    static List<string> ResolveFiles(CliOptions options)
    {
        var files = new List<string>();

        foreach (var pattern in options.FilePaths)
        {
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                var dir = Path.GetDirectoryName(pattern);
                if (string.IsNullOrEmpty(dir)) dir = ".";
                var filePattern = Path.GetFileName(pattern);
                files.AddRange(Directory.GetFiles(dir, filePattern));
            }
            else
            {
                if (File.Exists(pattern))
                    files.Add(Path.GetFullPath(pattern));
                else
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] File not found: {Markup.Escape(pattern)}");
            }
        }

        return files;
    }

    static CliOptions ParseArgs(string[] args)
    {
        var options = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":
                    options.Format = OutputFormat.Json;
                    break;
                case "--markdown" or "--md":
                    options.Format = OutputFormat.Markdown;
                    break;
                case "--quiet" or "-q":
                    options.Format = OutputFormat.Quiet;
                    break;
                case "--sample":
                    options.UseSample = true;
                    break;
                case "--severity":
                    if (i + 1 < args.Length && Enum.TryParse<FindingSeverity>(args[++i], true, out var sev))
                        options.MinSeverity = sev;
                    break;
                case "--output" or "-o":
                    if (i + 1 < args.Length)
                        options.OutputFile = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith('-'))
                        options.FilePaths.Add(args[i]);
                    break;
            }
        }

        return options;
    }

    static void PrintUsage()
    {
        AnsiConsole.Write(new FigletText("MSIXplainer").Color(Color.CornflowerBlue));

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").PadRight(4))
            .AddColumn("");

        table.AddRow("[cyan bold]Usage:[/]", "");
        table.AddRow("  msixplainer [grey]<file.msix>[/]", "Analyze a package");
        table.AddRow("  msixplainer [grey]*.msix[/]", "Analyze multiple packages");
        table.AddRow("  msixplainer [grey]--sample[/]", "Analyze built-in sample manifest");
        table.AddRow("", "");
        table.AddRow("[cyan bold]Output:[/]", "");
        table.AddRow("  [grey]--json[/]", "Output as JSON");
        table.AddRow("  [grey]--markdown, --md[/]", "Output as Markdown");
        table.AddRow("  [grey]--quiet, -q[/]", "No output, just exit code");
        table.AddRow("  [grey]--output, -o <file>[/]", "Write output to file");
        table.AddRow("", "");
        table.AddRow("[cyan bold]Filtering:[/]", "");
        table.AddRow("  [grey]--severity <level>[/]", "Minimum severity: info, review, warning, critical");
        table.AddRow("", "");
        table.AddRow("[cyan bold]Exit codes:[/]", "");
        table.AddRow("  [green]0[/]", "No warnings or critical findings");
        table.AddRow("  [yellow]1[/]", "One or more warnings");
        table.AddRow("  [red]2[/]", "One or more critical findings");
        table.AddRow("", "");
        table.AddRow("[cyan bold]Examples:[/]", "");
        table.AddRow("  msixplainer app.msix", "Full console report");
        table.AddRow("  msixplainer app.msix --json -o report.json", "Save JSON report");
        table.AddRow("  msixplainer app.msix --markdown -o review.md", "Save Markdown report");
        table.AddRow("  msixplainer *.msix --severity warning", "Only warnings+critical");
        table.AddRow("  msixplainer app.msix -q && echo PASS", "CI gate check");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}

sealed class CliOptions
{
    public List<string> FilePaths { get; } = [];
    public bool UseSample { get; set; }
    public OutputFormat Format { get; set; } = OutputFormat.Console;
    public FindingSeverity? MinSeverity { get; set; }
    public string? OutputFile { get; set; }

    public bool IsValid => UseSample || FilePaths.Count > 0;
}

enum OutputFormat
{
    Console,
    Json,
    Markdown,
    Quiet
}
