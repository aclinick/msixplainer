using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using MsixExplorer.Models;

namespace MsixExplorer.Services;

public static class ExportService
{
    // ────────────────────────────────────────────────────────────────
    //  Annotated Markdown — section-by-section manifest walkthrough
    // ────────────────────────────────────────────────────────────────

    public static string ExportToMarkdown(
        XElement manifestRoot,
        PackageInfo info,
        List<ManifestFinding> findings)
    {
        var sb = new StringBuilder();

        // ── Header ──
        sb.AppendLine($"# MSIX Manifest Review: {info.DisplayName}");
        sb.AppendLine();
        sb.AppendLine($"**Version:** {info.Version} · {info.Name} {info.Architecture}  ");
        sb.AppendLine($"**Publisher:** {info.PublisherLine}  ");
        if (!string.IsNullOrEmpty(info.MinOsVersion))
            sb.AppendLine($"**Min OS:** {info.MinOsVersion}  ");
        sb.AppendLine($"**Source:** AppxManifest.xml, static analysis  ");
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        // ── Risk assessment callout ──
        var riskLevel = info.CriticalCount > 0 ? "High"
            : info.WarningCount > 0 ? "Moderate"
            : info.ReviewCount > 0 ? "Low"
            : "Minimal";
        sb.AppendLine($"> **Overall risk: {riskLevel}.** This package has {info.CriticalCount} critical, " +
            $"{info.WarningCount} warning, {info.ReviewCount} review, and {info.InfoCount} informational findings.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // ── How to Read ──
        sb.AppendLine("## How to Read This Document");
        sb.AppendLine();
        sb.AppendLine("Each section presents the relevant manifest XML, followed by a table explaining what each element " +
            "does and what it means for IT review and deployment decisions.");
        sb.AppendLine();
        sb.AppendLine("| Column | What it covers |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| **Property** | The manifest element or attribute |");
        sb.AppendLine("| **Value** | What this package declares |");
        sb.AppendLine("| **What this means** | Plain-English explanation and any finding |");
        sb.AppendLine();
        sb.AppendLine("**Tags used in the \"What this means\" column:**");
        sb.AppendLine();
        sb.AppendLine("| Tag | Meaning |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| `🔴 CRITICAL` | Requires review before deployment. Elevated privileges or sensitive access. |");
        sb.AppendLine("| `🟡 WARNING` | Should be investigated. May indicate excessive permissions or unexpected behavior. |");
        sb.AppendLine("| `🔵 REVIEW` | Verify this matches the app's stated purpose. |");
        sb.AppendLine("| `ℹ️ INFO` | Standard practice. Awareness only. |");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // ── Build sections from manifest structure ──
        var sections = ManifestExplainerService.BuildSections(
            manifestRoot.Document!, findings, info.AppIconBytes);

        int sectionNum = 1;
        var usedFindings = new HashSet<ManifestFinding>();

        foreach (var section in sections)
        {
            if (section.Tag == "overview") continue;

            var groups = ManifestExplainerService.ExplainSection(
                section.Tag, manifestRoot, findings);
            if (groups.Count == 0) continue;

            // Section heading
            sb.AppendLine($"## Section {sectionNum:D2}: {section.Label}");
            sb.AppendLine();

            // Section XML snippet
            var xml = GetSectionXml(manifestRoot, section.Tag);
            if (!string.IsNullOrEmpty(xml))
            {
                sb.AppendLine("```xml");
                sb.AppendLine(xml);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            // Property groups
            foreach (var group in groups)
            {
                if (groups.Count > 1)
                {
                    sb.AppendLine($"### {group.Header}");
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(group.Description))
                {
                    sb.AppendLine($"> {group.Description}");
                    sb.AppendLine();
                }

                sb.AppendLine("| Property | Value | What this means |");
                sb.AppendLine("|---|---|---|");

                foreach (var prop in group.Properties)
                {
                    var meaning = Esc(prop.Explanation);
                    if (prop.HasFinding)
                    {
                        var f = prop.Finding!;
                        usedFindings.Add(f);
                        var tag = SeverityTag(f.Severity);
                        meaning = $"`{tag}` {Esc(f.Description)} " +
                            $"**Recommendation:** {Esc(f.Recommendation)}";
                    }
                    sb.AppendLine($"| {Esc(prop.Label)} | {Esc(prop.Value)} | {meaning} |");
                }
                sb.AppendLine();
            }

            // Standalone findings for this section that weren't linked to properties
            var sectionFindings = GetSectionFindings(section.Tag, findings)
                .Where(f => !usedFindings.Contains(f))
                .ToList();

            foreach (var f in sectionFindings)
            {
                usedFindings.Add(f);
                var tag = SeverityTag(f.Severity);
                sb.AppendLine($"**{tag}: {f.Title}**");
                sb.AppendLine();
                sb.AppendLine("| What it does | Why it matters | Recommendation |");
                sb.AppendLine("|---|---|---|");
                sb.AppendLine($"| {Esc(f.Description)} | {Esc(f.WhyItMatters)} | {Esc(f.Recommendation)} |");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(f.XmlSnippet))
                {
                    sb.AppendLine("```xml");
                    sb.AppendLine(f.XmlSnippet);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.AppendLine();
            sectionNum++;
        }

        // ── Findings Summary table ──
        sb.AppendLine("## Findings Summary");
        sb.AppendLine();
        sb.AppendLine("| # | Severity | Finding | Category | Recommendation |");
        sb.AppendLine("|---|---|---|---|---|");
        int n = 1;
        foreach (var f in findings.OrderByDescending(f => f.Severity))
        {
            sb.AppendLine($"| {n++} | `{SeverityTag(f.Severity)}` | {Esc(f.Title)} | " +
                $"{Esc(f.CategoryLabel)} | {Esc(f.Recommendation)} |");
        }
        sb.AppendLine();

        // ── Summary counts ──
        sb.AppendLine("| Severity | Count |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| 🔴 Critical | {info.CriticalCount} |");
        sb.AppendLine($"| 🟡 Warning | {info.WarningCount} |");
        sb.AppendLine($"| 🔵 Review | {info.ReviewCount} |");
        sb.AppendLine($"| ℹ️ Info | {info.InfoCount} |");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"*Generated by MSIX Manifest Explainer on {DateTime.Now:yyyy-MM-dd HH:mm}. " +
            "This document reflects what the manifest declares, not what the application does at runtime. " +
            "Actual runtime behavior would require install, launch, and uninstall tracing with tools such as ProcMon or ETW.*");

        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────────
    //  Markdown helpers
    // ────────────────────────────────────────────────────────────────

    private static string SeverityTag(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => "🔴 CRITICAL",
        FindingSeverity.Warning => "🟡 WARNING",
        FindingSeverity.Review => "🔵 REVIEW",
        _ => "ℹ️ INFO"
    };

    private static string Esc(string text) =>
        text.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");

    private static string? GetSectionXml(XElement root, string tag)
    {
        var ns = ManifestExplainerService.Ns;

        XElement? element = tag switch
        {
            "identity" => root.Element(ns + "Identity"),
            "properties" => root.Element(ns + "Properties"),
            "dependencies" => root.Element(ns + "Dependencies"),
            "resources" => root.Element(ns + "Resources"),
            "capabilities" => root.Element(ns + "Capabilities"),
            _ when tag.StartsWith("app:") => FindAppElement(root, tag[4..], ns),
            _ => null
        };

        if (element is null) return null;

        var xml = element.ToString();

        // Truncate very long sections (e.g., Application with many extensions)
        var lines = xml.Split('\n');
        if (lines.Length > 50)
        {
            xml = string.Join('\n', lines.Take(40))
                + "\n  <!-- ... additional elements omitted for brevity -->\n"
                + lines[^1];
        }

        return xml;
    }

    private static XElement? FindAppElement(XElement root, string appId, XNamespace ns)
    {
        return root.Element(ns + "Applications")
            ?.Elements(ns + "Application")
            .FirstOrDefault(e => e.Attribute("Id")?.Value == appId);
    }

    private static List<ManifestFinding> GetSectionFindings(string tag, List<ManifestFinding> findings)
    {
        FindingCategory[] categories = tag switch
        {
            "identity" => [FindingCategory.Identity],
            "properties" => [FindingCategory.Virtualization],
            "capabilities" => [FindingCategory.Capabilities, FindingCategory.DeviceAccess, FindingCategory.NetworkAccess],
            _ when tag.StartsWith("app:") =>
            [
                FindingCategory.Trust, FindingCategory.Startup, FindingCategory.Protocols,
                FindingCategory.FileAssociations, FindingCategory.BackgroundTasks,
                FindingCategory.COM, FindingCategory.OfficeIntegration, FindingCategory.WebView2
            ],
            _ => []
        };
        return findings.Where(f => categories.Contains(f.Category)).ToList();
    }

    public static string ExportToJson(PackageInfo info, List<ManifestFinding> findings)
    {
        var report = new
        {
            Package = new
            {
                info.Name,
                info.DisplayName,
                info.Version,
                info.Publisher,
                info.PublisherDisplayName,
                info.Architecture,
                info.MinOsVersion,
                info.MaxOsVersionTested
            },
            Summary = new
            {
                info.TotalFindings,
                info.CriticalCount,
                info.WarningCount,
                info.ReviewCount,
                info.InfoCount
            },
            Findings = findings.Select(f => new
            {
                f.Title,
                Severity = f.SeverityLabel,
                Category = f.CategoryLabel,
                f.Description,
                f.WhyItMatters,
                f.Recommendation,
                f.XmlSnippet
            }),
            GeneratedAt = DateTime.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}
