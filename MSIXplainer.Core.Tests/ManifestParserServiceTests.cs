using System.Xml.Linq;
using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class ManifestParserServiceTests
{
    [Fact]
    public void ParseRawXml_ValidManifest_ReturnsDocumentAndInfo()
    {
        var xml = SampleManifest.GetTeamsLikeManifest();

        var (manifest, rawXml, info) = ManifestParserService.ParseRawXml(xml);

        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Root);
        Assert.Equal(xml, rawXml);
        Assert.Equal("Contoso.CollaborationHub", info.Name);
        Assert.Equal("Contoso Collaboration Hub", info.DisplayName);
        Assert.Equal("24.10.1.100", info.Version);
        Assert.Equal("x64", info.Architecture);
        Assert.Equal("Contoso Ltd", info.PublisherDisplayName);
        Assert.Contains("CN=Contoso Ltd", info.Publisher);
        Assert.Equal("10.0.19041.0", info.MinOsVersion);
    }

    [Fact]
    public void ParseRawXml_ExtractsFrameworkDependencies()
    {
        var xml = SampleManifest.GetTeamsLikeManifest();

        var (_, _, info) = ManifestParserService.ParseRawXml(xml);

        Assert.Contains("Microsoft.VCLibs.140.00", info.FrameworkDependencies);
    }

    [Fact]
    public void ParseRawXml_MalformedXml_Throws()
    {
        Assert.ThrowsAny<Exception>(() =>
            ManifestParserService.ParseRawXml("<not-valid-xml>"));
    }

    [Fact]
    public void ParseRawXml_DtdInXml_ThrowsDueToSafeSettings()
    {
        var xmlWithDtd = """
            <?xml version="1.0"?>
            <!DOCTYPE Package [
              <!ENTITY xxe SYSTEM "file:///etc/passwd">
            ]>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="Test" Publisher="CN=Test" Version="1.0.0.0" />
            </Package>
            """;

        Assert.ThrowsAny<Exception>(() =>
            ManifestParserService.ParseRawXml(xmlWithDtd));
    }

    [Fact]
    public void ParseRawXml_MinimalManifest_ReturnsDefaults()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="Minimal.App" Publisher="CN=Test" Version="1.0.0.0" />
              <Properties>
                <DisplayName>Minimal</DisplayName>
                <Logo>Assets\Logo.png</Logo>
              </Properties>
            </Package>
            """;

        var (manifest, _, info) = ManifestParserService.ParseRawXml(xml);

        Assert.NotNull(manifest.Root);
        Assert.Equal("Minimal.App", info.Name);
        Assert.Equal("Minimal", info.DisplayName);
        Assert.Equal("1.0.0.0", info.Version);
    }
}
