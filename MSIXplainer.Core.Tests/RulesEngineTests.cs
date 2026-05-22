using System.Xml.Linq;
using MSIXplainer.Models;
using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class RulesEngineTests
{
    private static XDocument LoadSampleManifest()
    {
        var (manifest, _, _) = ManifestParserService.ParseRawXml(
            SampleManifest.GetTeamsLikeManifest());
        return manifest;
    }

    private static List<ManifestFinding> AnalyzeSample()
        => RulesEngine.Analyze(LoadSampleManifest());

    // ── Ordering ──

    [Fact]
    public void Analyze_ResultsOrderedBySeverityDescending()
    {
        var findings = AnalyzeSample();

        for (int i = 1; i < findings.Count; i++)
        {
            Assert.True(
                findings[i - 1].Severity >= findings[i].Severity
                || (findings[i - 1].Severity == findings[i].Severity
                    && findings[i - 1].Category <= findings[i].Category),
                $"Finding at index {i} is out of order: {findings[i - 1].Title} vs {findings[i].Title}");
        }
    }

    // ── Identity ──

    [Fact]
    public void Analyze_Sample_DetectsIdentity()
    {
        var findings = AnalyzeSample();
        var identity = findings.First(f =>
            f.Category == FindingCategory.Identity && f.Title == "Package Identity");

        Assert.Equal(FindingSeverity.Info, identity.Severity);
        Assert.Contains("Contoso.CollaborationHub", identity.Description);
    }

    // ── Trust Level ──

    [Fact]
    public void Analyze_Sample_DetectsFullTrust()
    {
        var findings = AnalyzeSample();
        var fullTrust = findings.First(f => f.Title == "Runs with Full Trust");

        Assert.Equal(FindingSeverity.Info, fullTrust.Severity);
        Assert.Equal(FindingCategory.Trust, fullTrust.Category);
    }

    [Fact]
    public void Analyze_SandboxedApp_DetectsAppContainer()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="Sandboxed.App" Publisher="CN=Test" Version="1.0.0.0" />
              <Capabilities>
                <Capability Name="internetClient" />
              </Capabilities>
            </Package>
            """;
        var (manifest, _, _) = ManifestParserService.ParseRawXml(xml);
        var findings = RulesEngine.Analyze(manifest);

        var sandbox = findings.First(f => f.Title == "Runs in AppContainer sandbox");
        Assert.Equal(FindingSeverity.Info, sandbox.Severity);
    }

    // ── Restricted Capabilities ──

    [Fact]
    public void Analyze_Sample_DetectsBroadFileSystemAccess()
    {
        var findings = AnalyzeSample();
        var bfsa = findings.First(f =>
            f.Title == "Restricted capability: broadFileSystemAccess");

        Assert.Equal(FindingSeverity.Warning, bfsa.Severity);
        Assert.Equal(FindingCategory.Capabilities, bfsa.Category);
    }

    [Fact]
    public void Analyze_Sample_DetectsAppDiagnostics()
    {
        var findings = AnalyzeSample();
        var diag = findings.First(f =>
            f.Title == "Restricted capability: appDiagnostics");

        Assert.Equal(FindingSeverity.Info, diag.Severity);
    }

    // ── Device Capabilities ──

    [Fact]
    public void Analyze_Sample_DetectsMicrophoneAndWebcam()
    {
        var findings = AnalyzeSample();

        Assert.Contains(findings, f => f.Title.Contains("microphone"));
        Assert.Contains(findings, f => f.Title.Contains("webcam"));
    }

    // ── Network ──

    [Fact]
    public void Analyze_Sample_DetectsNetworkCapabilities()
    {
        var findings = AnalyzeSample();
        var network = findings.Where(f => f.Category == FindingCategory.NetworkAccess).ToList();

        Assert.True(network.Count >= 3, "Sample should have at least 3 network findings");
        Assert.Contains(network, f => f.Title.Contains("internetClient"));
        Assert.Contains(network, f => f.Title.Contains("internetClientServer"));
        Assert.Contains(network, f => f.Title.Contains("privateNetworkClientServer"));
    }

    // ── Startup Tasks ──

    [Fact]
    public void Analyze_Sample_DetectsStartupTask()
    {
        var findings = AnalyzeSample();
        var startup = findings.First(f => f.Category == FindingCategory.Startup);

        Assert.Equal(FindingSeverity.Info, startup.Severity);
        Assert.Contains("ContosoHubStartup", startup.Description);
    }

    // ── Protocol Handlers ──

    [Fact]
    public void Analyze_Sample_DetectsProtocolHandlers()
    {
        var findings = AnalyzeSample();
        var protocols = findings.Where(f => f.Category == FindingCategory.Protocols).ToList();

        Assert.True(protocols.Count >= 3, "Sample should have contoso-hub, contoso-meeting, contoso-call");
    }

    // ── File Associations ──

    [Fact]
    public void Analyze_Sample_DetectsFileAssociations()
    {
        var findings = AnalyzeSample();
        Assert.Contains(findings, f =>
            f.Category == FindingCategory.FileAssociations);
    }

    // ── Virtualization ──

    [Fact]
    public void Analyze_Sample_DetectsVirtualizationDisabled()
    {
        var findings = AnalyzeSample();
        var virt = findings.Where(f => f.Category == FindingCategory.Virtualization).ToList();

        Assert.True(virt.Count >= 2, "Should detect both filesystem and registry virtualization disabled");
        Assert.All(virt, f => Assert.Equal(FindingSeverity.Critical, f.Severity));
    }

    // ── COM Registrations ──

    [Fact]
    public void Analyze_Sample_DetectsComRegistrations()
    {
        var findings = AnalyzeSample();
        Assert.Contains(findings, f =>
            f.Category == FindingCategory.COM);
    }

    // ── Background Tasks ──

    [Fact]
    public void Analyze_Sample_DetectsBackgroundTasks()
    {
        var findings = AnalyzeSample();
        Assert.Contains(findings, f =>
            f.Category == FindingCategory.BackgroundTasks);
    }

    // ── App URI Handlers ──

    [Fact]
    public void Analyze_Sample_DetectsAppUriHandlers()
    {
        var findings = AnalyzeSample();
        var uri = findings.Where(f =>
            f.Title.Contains("URI") || f.Title.Contains("web link")).ToList();

        Assert.NotEmpty(uri);
    }

    // ── Empty/Minimal Manifest ──

    [Fact]
    public void Analyze_MinimalManifest_ProducesMinimalFindings()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="Minimal.App" Publisher="CN=Test" Version="1.0.0.0" />
            </Package>
            """;
        var (manifest, _, _) = ManifestParserService.ParseRawXml(xml);
        var findings = RulesEngine.Analyze(manifest);

        // Should have identity + sandbox info, no warnings or criticals
        Assert.All(findings, f =>
            Assert.True(f.Severity <= FindingSeverity.Review,
                $"Minimal manifest should not produce warnings/criticals, got: {f.Title}"));
    }

    // ── Finding properties are complete ──

    [Fact]
    public void Analyze_AllFindings_HaveRequiredFields()
    {
        var findings = AnalyzeSample();

        Assert.All(findings, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Title), "Title must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(f.Description), "Description must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(f.WhyItMatters), "WhyItMatters must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(f.Recommendation), "Recommendation must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(f.SeverityLabel), "SeverityLabel must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(f.CategoryLabel), "CategoryLabel must not be empty");
        });
    }

    // ── Sample covers all major categories ──

    [Fact]
    public void Analyze_Sample_CoversExpectedCategories()
    {
        var findings = AnalyzeSample();
        var categories = findings.Select(f => f.Category).Distinct().ToHashSet();

        Assert.Contains(FindingCategory.Identity, categories);
        Assert.Contains(FindingCategory.Trust, categories);
        Assert.Contains(FindingCategory.Capabilities, categories);
        Assert.Contains(FindingCategory.DeviceAccess, categories);
        Assert.Contains(FindingCategory.NetworkAccess, categories);
        Assert.Contains(FindingCategory.Startup, categories);
        Assert.Contains(FindingCategory.Protocols, categories);
        Assert.Contains(FindingCategory.Virtualization, categories);
        Assert.Contains(FindingCategory.COM, categories);
        Assert.Contains(FindingCategory.BackgroundTasks, categories);
    }

    // ── Severity counts ──

    [Fact]
    public void Analyze_Sample_HasExpectedSeverityDistribution()
    {
        var findings = AnalyzeSample();

        // Sample manifest disables filesystem and registry virtualization, which
        // produces 2 Critical findings under the recalibrated severity model.
        Assert.True(findings.Count(f => f.Severity == FindingSeverity.Critical) >= 2,
            "Sample should have at least 2 critical findings (filesystem + registry virtualization disabled)");
        Assert.True(findings.Count(f => f.Severity == FindingSeverity.Warning) >= 1,
            "Sample should have at least one warning finding");
        Assert.True(findings.Count > 10,
            "Sample manifest should produce many findings");
    }
}
