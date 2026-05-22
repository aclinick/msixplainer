using MSIXplainer.Models;
using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class RuleSeverityOverridesTests
{
    [Fact]
    public void Empty_Resolve_ReturnsDefault()
    {
        var overrides = RuleSeverityOverrides.Empty;

        Assert.Equal(FindingSeverity.Critical,
            overrides.Resolve("trust.fullTrust", FindingSeverity.Critical));
        Assert.Empty(overrides.Effective);
    }

    [Fact]
    public void Parse_ValidJson_AppliesOverrides()
    {
        var json = """
            {
              "trust.fullTrust": "Info",
              "services.windowsService": "Warning"
            }
            """;

        var overrides = RuleSeverityOverrides.Parse(json, "test", RuleCatalog.KnownRuleIds);

        Assert.Equal(FindingSeverity.Info,
            overrides.Resolve("trust.fullTrust", FindingSeverity.Critical));
        Assert.Equal(FindingSeverity.Warning,
            overrides.Resolve("services.windowsService", FindingSeverity.Critical));
        Assert.Equal(FindingSeverity.Review,
            overrides.Resolve("not.overridden", FindingSeverity.Review));
    }

    [Fact]
    public void Parse_UnknownSeverity_Warns_AndSkips()
    {
        var json = """{ "trust.fullTrust": "Bogus" }""";
        var warnings = new List<string>();

        var overrides = RuleSeverityOverrides.Parse(
            json, "test", RuleCatalog.KnownRuleIds, warnings.Add);

        Assert.Empty(overrides.Effective);
        Assert.Single(warnings);
        Assert.Contains("Bogus", warnings[0]);
    }

    [Fact]
    public void Parse_UnknownRuleId_Warns_AndSkips()
    {
        var json = """{ "made.up.rule": "Info" }""";
        var warnings = new List<string>();

        var overrides = RuleSeverityOverrides.Parse(
            json, "test", RuleCatalog.KnownRuleIds, warnings.Add);

        Assert.Empty(overrides.Effective);
        Assert.Single(warnings);
        Assert.Contains("made.up.rule", warnings[0]);
    }

    [Fact]
    public void Parse_DynamicCapabilityId_IsAccepted_ViaWildcard()
    {
        var json = """{ "capability.broadFileSystemAccess": "Info" }""";
        var warnings = new List<string>();

        var overrides = RuleSeverityOverrides.Parse(
            json, "test", RuleCatalog.KnownRuleIds, warnings.Add);

        Assert.Empty(warnings);
        Assert.Equal(FindingSeverity.Info,
            overrides.Resolve("capability.broadFileSystemAccess", FindingSeverity.Critical));
    }

    [Fact]
    public void Parse_NonObjectRoot_Warns_ReturnsEmpty()
    {
        var warnings = new List<string>();

        var overrides = RuleSeverityOverrides.Parse(
            "[]", "test", RuleCatalog.KnownRuleIds, warnings.Add);

        Assert.Empty(overrides.Effective);
        Assert.Single(warnings);
    }

    [Fact]
    public void LoadFromFile_Missing_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.json");

        var overrides = RuleSeverityOverrides.LoadFromFile(path);

        Assert.Same(RuleSeverityOverrides.Empty, overrides);
    }

    [Fact]
    public void LoadFromFile_Present_ParsesAndTracksSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rules-{Guid.NewGuid()}.json");
        File.WriteAllText(path, """{ "trust.fullTrust": "Critical" }""");

        try
        {
            var overrides = RuleSeverityOverrides.LoadFromFile(path, RuleCatalog.KnownRuleIds);

            Assert.Equal(FindingSeverity.Critical,
                overrides.Resolve("trust.fullTrust", FindingSeverity.Info));
            Assert.Equal(path, overrides.Sources["trust.fullTrust"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Merge_LaterLayer_WinsOverEarlier()
    {
        var earlier = RuleSeverityOverrides.Parse(
            """{ "trust.fullTrust": "Info", "services.windowsService": "Critical" }""",
            "earlier", RuleCatalog.KnownRuleIds);
        var later = RuleSeverityOverrides.Parse(
            """{ "trust.fullTrust": "Critical" }""",
            "later", RuleCatalog.KnownRuleIds);

        var merged = RuleSeverityOverrides.Merge(earlier, later);

        Assert.Equal(FindingSeverity.Critical,
            merged.Resolve("trust.fullTrust", FindingSeverity.Info));
        Assert.Equal("later", merged.Sources["trust.fullTrust"]);

        Assert.Equal(FindingSeverity.Critical,
            merged.Resolve("services.windowsService", FindingSeverity.Info));
        Assert.Equal("earlier", merged.Sources["services.windowsService"]);
    }

    [Fact]
    public void RuleId_Lookup_IsCaseInsensitive()
    {
        var json = """{ "Trust.FullTrust": "Info" }""";

        var overrides = RuleSeverityOverrides.Parse(json, "test", RuleCatalog.KnownRuleIds);

        Assert.Equal(FindingSeverity.Info,
            overrides.Resolve("trust.fullTrust", FindingSeverity.Critical));
    }

    [Fact]
    public void DefaultUserPath_IsUnderLocalAppData()
    {
        var path = RuleSeverityOverrides.DefaultUserPath;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("rules.json", path, StringComparison.OrdinalIgnoreCase);
    }
}
