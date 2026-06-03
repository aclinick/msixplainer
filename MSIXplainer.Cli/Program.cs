using System.Text;
using MSIXplainer.Models;
using MSIXplainer.Services;
using Spectre.Console;

namespace MSIXplainer;

static class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        // Subcommands handle their own --help.
        if (string.Equals(args[0], "rules", StringComparison.OrdinalIgnoreCase))
        {
            return RunRulesSubcommand(args.Skip(1).ToArray());
        }

        if (string.Equals(args[0], "diff", StringComparison.OrdinalIgnoreCase))
        {
            return DiffCommand.Run(args.Skip(1).ToArray());
        }

        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        var options = ParseArgs(args);

        if (!options.IsValid)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Provide a path to an .msix/.appx/.msixbundle file, or use --sample.");
            return 1;
        }

        options.Overrides = LoadOverrides(options);

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
            if (ManifestParserService.IsBundleFile(file))
            {
                var exit = AnalyzeBundle(file, options);
                if (exit > worstExit) worstExit = exit;
            }
            else
            {
                if (files.Count > 1 || options.UseSample)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(Path.GetFileName(file))}[/]").LeftJustified());
                }

                var exit = AnalyzeOne(file, options);
                if (exit > worstExit) worstExit = exit;
            }
        }

        return worstExit;
    }

    static RuleSeverityOverrides LoadOverrides(CliOptions options)
    {
        var layers = new List<RuleSeverityOverrides>();
        var quiet = options.Format == OutputFormat.Quiet;
        Action<string>? warn = quiet
            ? null
            : msg => AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(msg)}");

        if (File.Exists(RuleSeverityOverrides.DefaultUserPath))
        {
            layers.Add(RuleSeverityOverrides.LoadFromFile(
                RuleSeverityOverrides.DefaultUserPath,
                RuleCatalog.KnownRuleIds,
                warn));
        }

        if (!string.IsNullOrEmpty(options.RulesFile))
        {
            if (!File.Exists(options.RulesFile))
            {
                if (!quiet)
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] Rules file not found: {Markup.Escape(options.RulesFile)}");
            }
            else
            {
                layers.Add(RuleSeverityOverrides.LoadFromFile(
                    options.RulesFile,
                    RuleCatalog.KnownRuleIds,
                    warn));
            }
        }

        return layers.Count == 0
            ? RuleSeverityOverrides.Empty
            : RuleSeverityOverrides.Merge([.. layers]);
    }

    static int RunRulesSubcommand(string[] args)
    {
        if (args.Length == 0 || string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            return PrintRulesList(args.Skip(1).ToArray());
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] Unknown rules subcommand: {Markup.Escape(args[0])}");
        AnsiConsole.MarkupLine("Available: [cyan]rules list[/]");
        return 1;
    }

    static int PrintRulesList(string[] args)
    {
        string? rulesFile = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--rules" && i + 1 < args.Length)
                rulesFile = args[++i];
        }

        var layers = new List<RuleSeverityOverrides>();
        Action<string> warn = msg => AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(msg)}");

        if (File.Exists(RuleSeverityOverrides.DefaultUserPath))
        {
            layers.Add(RuleSeverityOverrides.LoadFromFile(
                RuleSeverityOverrides.DefaultUserPath, RuleCatalog.KnownRuleIds, warn));
        }
        if (!string.IsNullOrEmpty(rulesFile) && File.Exists(rulesFile))
        {
            layers.Add(RuleSeverityOverrides.LoadFromFile(
                rulesFile, RuleCatalog.KnownRuleIds, warn));
        }

        var effective = layers.Count == 0
            ? RuleSeverityOverrides.Empty
            : RuleSeverityOverrides.Merge([.. layers]);

        AnsiConsole.WriteLine();
        var defaultPath = RuleSeverityOverrides.DefaultUserPath;
        AnsiConsole.MarkupLine($"[grey]User rules file:[/] [link]{Markup.Escape(defaultPath)}[/] " +
            (File.Exists(defaultPath) ? "[green](found)[/]" : "[grey](not present)[/]"));
        if (!string.IsNullOrEmpty(rulesFile))
        {
            AnsiConsole.MarkupLine($"[grey]Extra rules file:[/] [link]{Markup.Escape(rulesFile)}[/] " +
                (File.Exists(rulesFile) ? "[green](found)[/]" : "[red](not found)[/]"));
        }
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Rule ID[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Category[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Default[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Effective[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Source[/]").NoWrap())
            .AddColumn("[grey]Description[/]");

        foreach (var entry in RuleCatalog.All)
        {
            var defaultSev = entry.DefaultSeverity;
            var effectiveSev = effective.Resolve(entry.RuleId, defaultSev);
            var source = effective.Sources.TryGetValue(entry.RuleId, out var s)
                ? Path.GetFileName(s)
                : "built-in";
            var rowStyle = effectiveSev == defaultSev ? string.Empty : "yellow ";

            table.AddRow(
                $"[{rowStyle}cyan]{Markup.Escape(entry.RuleId)}[/]",
                Markup.Escape(entry.Category.ToString()),
                FormatSeverityCell(defaultSev),
                FormatSeverityCell(effectiveSev),
                Markup.Escape(source),
                Markup.Escape(entry.Description));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[grey]To customize, create the user rules file with JSON like:[/]");
        AnsiConsole.MarkupLine("  [cyan]{[/]");
        AnsiConsole.MarkupLine("    [cyan]\"trust.fullTrust\": \"Info\",[/]");
        AnsiConsole.MarkupLine("    [cyan]\"services.windowsService\": \"Warning\"[/]");
        AnsiConsole.MarkupLine("  [cyan]}[/]");
        AnsiConsole.WriteLine();

        return 0;
    }

    static string FormatSeverityCell(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => "[red bold]Critical[/]",
        FindingSeverity.Warning => "[yellow bold]Warning[/]",
        FindingSeverity.Review => "[cyan]Review[/]",
        _ => "[grey]Info[/]"
    };

    static int AnalyzeBundle(string filePath, CliOptions options)
    {
        int worstExit = 0;

        try
        {
            var packages = AnsiConsole.Status()
                .AutoRefresh(true)
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start("Opening bundle...", ctx =>
                {
                    ctx.Status($"Extracting packages from {Path.GetFileName(filePath)}...");
                    return ManifestParserService.ExtractFromBundle(filePath);
                });

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(Path.GetFileName(filePath))}[/] [grey]({packages.Count} package(s))[/]").LeftJustified());

            foreach (var pkg in packages)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(pkg.Label)}[/] [grey]({pkg.Info.Architecture})[/]").LeftJustified());

                var findings = RulesEngine.Analyze(pkg.Manifest, options.Overrides);
                pkg.Info.CriticalCount = findings.Count(f => f.Severity == FindingSeverity.Critical);
                pkg.Info.WarningCount = findings.Count(f => f.Severity == FindingSeverity.Warning);
                pkg.Info.ReviewCount = findings.Count(f => f.Severity == FindingSeverity.Review);
                pkg.Info.InfoCount = findings.Count(f => f.Severity == FindingSeverity.Info);

                if (options.MinSeverity is not null)
                    findings = findings.Where(f => f.Severity >= options.MinSeverity.Value).ToList();

                switch (options.Format)
                {
                    case OutputFormat.Json:
                        Write(options, ExportService.ExportToJson(pkg.Info, findings));
                        break;
                    case OutputFormat.Markdown:
                        Write(options, ExportService.ExportToMarkdown(pkg.Manifest.Root!, pkg.Info, findings));
                        break;
                    case OutputFormat.Quiet:
                        break;
                    default:
                        PrintConsoleReport(pkg.Info, findings);
                        break;
                }

                var exit = pkg.Info.CriticalCount > 0 ? 2 : pkg.Info.WarningCount > 0 ? 1 : 0;
                if (exit > worstExit) worstExit = exit;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
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
                    var f = RulesEngine.Analyze(doc, options.Overrides);

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
        if (!string.IsNullOrEmpty(info.PackageFamilyName))
            table.AddRow("Family Name", Markup.Escape(info.PackageFamilyName));
        if (!string.IsNullOrEmpty(info.PackageFullName))
            table.AddRow("Full Name", Markup.Escape(info.PackageFullName));
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
                case "--rules":
                    if (i + 1 < args.Length)
                        options.RulesFile = args[++i];
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
        table.AddRow("  msixplainer [grey]<file.msixbundle>[/]", "Analyze all packages in a bundle");
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
        table.AddRow("[cyan bold]Rule customization:[/]", "");
        table.AddRow("  [grey]--rules <file>[/]", "Override per-rule severities from a JSON file");
        table.AddRow("  msixplainer rules list", "Show all rule IDs and their default/effective severity");
        table.AddRow("", $"[grey]Auto-loaded:[/] {Markup.Escape(RuleSeverityOverrides.DefaultUserPath)}");
        table.AddRow("", "");
        table.AddRow("[cyan bold]Update impact (diff between two versions):[/]", "");
        table.AddRow("  msixplainer diff [grey]<old> <new>[/]", "Show update download size between two packages or bundles");
        table.AddRow("  [grey]--devices N[/]", "Devices in the rollout (enables bandwidth planner)");
        table.AddRow("  [grey]--link 100,1000[/]", "Link speeds in Mbps (comma separated, default 100,1000)");
        table.AddRow("  [grey]--cost 0.08[/]", "Egress cost per GB (USD)");
        table.AddRow("  [grey]--top N[/]", "Show top N changed files (default 25)");
        table.AddRow("  [grey]--markdown[/] / [grey]--json[/] / [grey]-o <file>[/]", "Same output options as analyse");
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
        table.AddRow("  msixplainer diff v1.0.msix v1.1.msix", "Show update download size");
        table.AddRow("  msixplainer diff v1.0.msix v1.1.msix --devices 500", "With bandwidth planner");
        table.AddRow("  msixplainer diff v1.0.msix v1.1.msix --markdown -o update.md", "Save Markdown report");

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
    public string? RulesFile { get; set; }
    public RuleSeverityOverrides Overrides { get; set; } = RuleSeverityOverrides.Empty;

    public bool IsValid => UseSample || FilePaths.Count > 0;
}

enum OutputFormat
{
    Console,
    Json,
    Markdown,
    Quiet
}
