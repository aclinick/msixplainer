using MSIXplainer.Models;
using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class RuleCatalogTests
{
    [Fact]
    public void All_Entries_HaveUniqueRuleIds()
    {
        var duplicates = RuleCatalog.All
            .GroupBy(e => e.RuleId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void All_Entries_HaveNonEmptyFields()
    {
        foreach (var entry in RuleCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.RuleId), $"Rule ID is empty");
            Assert.False(string.IsNullOrWhiteSpace(entry.Description),
                $"Description is empty for {entry.RuleId}");
        }
    }

    [Fact]
    public void EveryRuleEmittedBySample_HasCatalogEntry()
    {
        var (manifest, _, _) = ManifestParserService.ParseRawXml(
            SampleManifest.GetTeamsLikeManifest());
        var findings = RulesEngine.Analyze(manifest);

        var missing = findings
            .Where(f => !string.IsNullOrEmpty(f.RuleId))
            .Select(f => f.RuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => RuleCatalog.FindEntry(id) is null)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryFindingFromSample_HasRuleId()
    {
        var (manifest, _, _) = ManifestParserService.ParseRawXml(
            SampleManifest.GetTeamsLikeManifest());
        var findings = RulesEngine.Analyze(manifest);

        var withoutId = findings.Where(f => string.IsNullOrEmpty(f.RuleId)).ToList();

        Assert.Empty(withoutId);
    }
}

public class RulesEngineOverrideTests
{
    private static System.Xml.Linq.XDocument LoadSample()
    {
        var (manifest, _, _) = ManifestParserService.ParseRawXml(
            SampleManifest.GetTeamsLikeManifest());
        return manifest;
    }

    [Fact]
    public void Analyze_WithoutOverrides_MatchesEmptyOverrides()
    {
        var manifest = LoadSample();
        var baseline = RulesEngine.Analyze(manifest);
        var withEmpty = RulesEngine.Analyze(manifest, RuleSeverityOverrides.Empty);

        Assert.Equal(baseline.Count, withEmpty.Count);
        for (int i = 0; i < baseline.Count; i++)
        {
            Assert.Equal(baseline[i].RuleId, withEmpty[i].RuleId);
            Assert.Equal(baseline[i].Severity, withEmpty[i].Severity);
        }
    }

    [Fact]
    public void Analyze_WithOverride_ChangesSeverityForMatchedRule()
    {
        var manifest = LoadSample();
        var overrides = RuleSeverityOverrides.Parse(
            """{ "trust.fullTrust": "Critical" }""",
            "test", RuleCatalog.KnownRuleIds);

        var findings = RulesEngine.Analyze(manifest, overrides);
        var fullTrust = findings.First(f => f.RuleId == "trust.fullTrust");

        Assert.Equal(FindingSeverity.Critical, fullTrust.Severity);
    }

    [Fact]
    public void Analyze_WithOverride_LeavesOtherRulesUnchanged()
    {
        var manifest = LoadSample();
        var baseline = RulesEngine.Analyze(manifest);
        var overrides = RuleSeverityOverrides.Parse(
            """{ "trust.fullTrust": "Critical" }""",
            "test", RuleCatalog.KnownRuleIds);

        var findings = RulesEngine.Analyze(manifest, overrides);

        // All non-overridden rules keep their default severity.
        foreach (var baselineFinding in baseline.Where(f => f.RuleId != "trust.fullTrust"))
        {
            var match = findings.First(f =>
                f.RuleId == baselineFinding.RuleId && f.Title == baselineFinding.Title);
            Assert.Equal(baselineFinding.Severity, match.Severity);
        }
    }

    [Fact]
    public void Analyze_WithOverride_AffectsSortOrder()
    {
        var manifest = LoadSample();
        var overrides = RuleSeverityOverrides.Parse(
            """{ "trust.fullTrust": "Critical" }""",
            "test", RuleCatalog.KnownRuleIds);

        var findings = RulesEngine.Analyze(manifest, overrides);

        var firstCritical = findings.First();
        Assert.Equal(FindingSeverity.Critical, firstCritical.Severity);
    }

    [Fact]
    public void Analyze_DowngradeAllCriticals_ToInfo()
    {
        var manifest = LoadSample();
        var overrides = RuleSeverityOverrides.Parse(
            """
            {
              "virt.filesystemDisabled": "Info",
              "virt.registryDisabled": "Info"
            }
            """,
            "test", RuleCatalog.KnownRuleIds);

        var findings = RulesEngine.Analyze(manifest, overrides);

        Assert.Equal(0, findings.Count(f => f.Severity == FindingSeverity.Critical));
    }
}
