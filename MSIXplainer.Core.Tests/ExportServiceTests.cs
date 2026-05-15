using System.Text.Json;
using MSIXplainer.Models;
using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class ExportServiceTests
{
    private static (PackageInfo Info, List<ManifestFinding> Findings, System.Xml.Linq.XElement Root) LoadSample()
    {
        var (manifest, _, info) = ManifestParserService.ParseRawXml(
            SampleManifest.GetTeamsLikeManifest());
        var findings = RulesEngine.Analyze(manifest);
        info.CriticalCount = findings.Count(f => f.Severity == FindingSeverity.Critical);
        info.WarningCount = findings.Count(f => f.Severity == FindingSeverity.Warning);
        info.ReviewCount = findings.Count(f => f.Severity == FindingSeverity.Review);
        info.InfoCount = findings.Count(f => f.Severity == FindingSeverity.Info);
        return (info, findings, manifest.Root!);
    }

    // ── Markdown Export ──

    [Fact]
    public void ExportToMarkdown_ContainsPackageName()
    {
        var (info, findings, root) = LoadSample();
        var md = ExportService.ExportToMarkdown(root, info, findings);

        Assert.Contains("Contoso Collaboration Hub", md);
    }

    [Fact]
    public void ExportToMarkdown_ContainsSeverityTags()
    {
        var (info, findings, root) = LoadSample();
        var md = ExportService.ExportToMarkdown(root, info, findings);

        Assert.Contains("🔴 CRITICAL", md);
        Assert.Contains("🟡 WARNING", md);
    }

    [Fact]
    public void ExportToMarkdown_ContainsSectionHeadings()
    {
        var (info, findings, root) = LoadSample();
        var md = ExportService.ExportToMarkdown(root, info, findings);

        Assert.Contains("## Section 01:", md);
        Assert.Contains("## Findings Summary", md);
    }

    [Fact]
    public void ExportToMarkdown_ContainsRiskAssessment()
    {
        var (info, findings, root) = LoadSample();
        var md = ExportService.ExportToMarkdown(root, info, findings);

        Assert.Contains("Overall risk:", md);
        Assert.Contains("High", md); // Sample has critical findings
    }

    [Fact]
    public void ExportToMarkdown_ContainsHowToRead()
    {
        var (info, findings, root) = LoadSample();
        var md = ExportService.ExportToMarkdown(root, info, findings);

        Assert.Contains("How to Read This Document", md);
    }

    [Fact]
    public void ExportToMarkdown_ContainsXmlCodeBlocks()
    {
        var (info, findings, root) = LoadSample();
        var md = ExportService.ExportToMarkdown(root, info, findings);

        Assert.Contains("```xml", md);
    }

    // ── JSON Export ──

    [Fact]
    public void ExportToJson_IsValidJson()
    {
        var (info, findings, _) = LoadSample();
        var json = ExportService.ExportToJson(info, findings);

        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public void ExportToJson_ContainsPackageInfo()
    {
        var (info, findings, _) = LoadSample();
        var json = ExportService.ExportToJson(info, findings);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Contoso.CollaborationHub",
            root.GetProperty("Package").GetProperty("Name").GetString());
        Assert.Equal("24.10.1.100",
            root.GetProperty("Package").GetProperty("Version").GetString());
    }

    [Fact]
    public void ExportToJson_ContainsSummaryCounts()
    {
        var (info, findings, _) = LoadSample();
        var json = ExportService.ExportToJson(info, findings);
        var doc = JsonDocument.Parse(json);
        var summary = doc.RootElement.GetProperty("Summary");

        Assert.True(summary.GetProperty("CriticalCount").GetInt32() >= 2);
        Assert.True(summary.GetProperty("TotalFindings").GetInt32() > 10);
    }

    [Fact]
    public void ExportToJson_FindingsHaveAllFields()
    {
        var (info, findings, _) = LoadSample();
        var json = ExportService.ExportToJson(info, findings);
        var doc = JsonDocument.Parse(json);

        foreach (var finding in doc.RootElement.GetProperty("Findings").EnumerateArray())
        {
            Assert.True(finding.TryGetProperty("Title", out _));
            Assert.True(finding.TryGetProperty("Severity", out _));
            Assert.True(finding.TryGetProperty("Category", out _));
            Assert.True(finding.TryGetProperty("Description", out _));
            Assert.True(finding.TryGetProperty("Recommendation", out _));
        }
    }
}
