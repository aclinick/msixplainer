using System.Xml.Linq;
using MSIXplainer.Models;
using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class ManifestExplainerServiceTests
{
    private static (XDocument Manifest, List<ManifestFinding> Findings) LoadSample()
    {
        var (manifest, _, _) = ManifestParserService.ParseRawXml(
            SampleManifest.GetTeamsLikeManifest());
        var findings = RulesEngine.Analyze(manifest);
        return (manifest, findings);
    }

    [Fact]
    public void BuildSections_IncludesOverview()
    {
        var (manifest, findings) = LoadSample();
        var sections = ManifestExplainerService.BuildSections(manifest, findings);

        var overview = sections.First(s => s.Tag == "overview");
        Assert.Equal("Overview", overview.Label);
        Assert.Equal(findings.Count, overview.FindingCount);
    }

    [Fact]
    public void BuildSections_IncludesIdentitySection()
    {
        var (manifest, findings) = LoadSample();
        var sections = ManifestExplainerService.BuildSections(manifest, findings);

        Assert.Contains(sections, s => s.Tag == "identity");
    }

    [Fact]
    public void BuildSections_IncludesCapabilitiesSection()
    {
        var (manifest, findings) = LoadSample();
        var sections = ManifestExplainerService.BuildSections(manifest, findings);

        Assert.Contains(sections, s => s.Tag == "capabilities");
    }

    [Fact]
    public void BuildSections_IncludesApplicationSection()
    {
        var (manifest, findings) = LoadSample();
        var sections = ManifestExplainerService.BuildSections(manifest, findings);

        Assert.Contains(sections, s => s.Tag == "applications");
    }

    [Fact]
    public void BuildSections_SectionCountMatchesManifestElements()
    {
        var (manifest, findings) = LoadSample();
        var sections = ManifestExplainerService.BuildSections(manifest, findings);

        // Overview + Identity + Properties + Dependencies + Resources + Capabilities + App
        Assert.True(sections.Count >= 6,
            $"Expected at least 6 sections for sample manifest, got {sections.Count}");
    }

    [Fact]
    public void ExplainSection_Identity_ReturnsPropertyGroups()
    {
        var (manifest, findings) = LoadSample();
        var groups = ManifestExplainerService.ExplainSection(
            "identity", manifest.Root!, findings);

        Assert.NotEmpty(groups);
        var props = groups.SelectMany(g => g.Properties).ToList();
        Assert.Contains(props, p => p.Label.Contains("Name"));
        Assert.Contains(props, p => p.Value.Contains("Contoso"));
    }

    [Fact]
    public void ExplainSection_Capabilities_ReturnsFindings()
    {
        var (manifest, findings) = LoadSample();
        var groups = ManifestExplainerService.ExplainSection(
            "capabilities", manifest.Root!, findings);

        Assert.NotEmpty(groups);
        var propsWithFindings = groups
            .SelectMany(g => g.Properties)
            .Where(p => p.HasFinding)
            .ToList();
        Assert.NotEmpty(propsWithFindings);
    }

    [Fact]
    public void BuildSections_OverviewWorstSeverity_IsCriticalForSample()
    {
        var (manifest, findings) = LoadSample();
        var sections = ManifestExplainerService.BuildSections(manifest, findings);
        var overview = sections.First(s => s.Tag == "overview");

        Assert.Equal(FindingSeverity.Critical, overview.WorstSeverity);
    }

    [Fact]
    public void BuildSections_MinimalManifest_HasFewerSections()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="Minimal.App" Publisher="CN=Test" Version="1.0.0.0" />
            </Package>
            """;
        var (manifest, _, _) = ManifestParserService.ParseRawXml(xml);
        var findings = RulesEngine.Analyze(manifest);
        var sections = ManifestExplainerService.BuildSections(manifest, findings);

        // Overview + Identity only
        Assert.True(sections.Count <= 3,
            $"Minimal manifest should have few sections, got {sections.Count}");
    }
}
