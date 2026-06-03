using MSIXplainer.Services;
using Xunit;

namespace MSIXplainer.Core.Tests;

public class ExtractFromManifestFileTests
{
    private const string SampleManifest = """
    <?xml version="1.0" encoding="utf-8"?>
    <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
      <Identity Name="Contoso.Demo" Publisher="CN=Contoso" Version="2.5.0.0" ProcessorArchitecture="x64" />
      <Properties>
        <DisplayName>Contoso Demo</DisplayName>
        <PublisherDisplayName>Contoso</PublisherDisplayName>
        <Description>A demo package.</Description>
      </Properties>
      <Dependencies>
        <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.22621.0" />
      </Dependencies>
      <Applications>
        <Application Id="App" Executable="Demo.exe" EntryPoint="Windows.FullTrustApplication" />
      </Applications>
    </Package>
    """;

    [Fact]
    public void ExtractFromManifestFile_ReadsLooseManifest()
    {
        var dir = Directory.CreateTempSubdirectory("msixplainer-manifest-test-");
        try
        {
            var path = Path.Combine(dir.FullName, "AppxManifest.xml");
            File.WriteAllText(path, SampleManifest);

            var (doc, raw, info) = ManifestParserService.ExtractFromManifestFile(path);

            Assert.NotNull(doc.Root);
            Assert.Equal("Contoso.Demo", info.Name);
            Assert.Equal("Contoso Demo", info.DisplayName);
            Assert.Equal("2.5.0.0", info.Version);
            Assert.Equal("x64", info.Architecture);
            Assert.StartsWith("Contoso.Demo_", info.PackageFamilyName);
            Assert.Equal(13, info.PackageFamilyName.Split('_')[1].Length);
            Assert.Contains("<Identity", raw);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExtractFromManifestFile_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ManifestParserService.ExtractFromManifestFile(
                Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".xml")));
    }

    [Fact]
    public void ExtractFromManifestFile_DtdProcessing_Rejected()
    {
        var dir = Directory.CreateTempSubdirectory("msixplainer-manifest-dtd-");
        try
        {
            var path = Path.Combine(dir.FullName, "AppxManifest.xml");
            var malicious = """
            <?xml version="1.0"?>
            <!DOCTYPE Package [<!ENTITY bomb "boom">]>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="X" Publisher="CN=X" Version="1.0.0.0" ProcessorArchitecture="x64" />
            </Package>
            """;
            File.WriteAllText(path, malicious);

            Assert.ThrowsAny<System.Xml.XmlException>(() =>
                ManifestParserService.ExtractFromManifestFile(path));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
