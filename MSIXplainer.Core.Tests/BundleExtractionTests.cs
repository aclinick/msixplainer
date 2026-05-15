using System.IO.Compression;
using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class BundleExtractionTests
{
    private static readonly string MinimalManifestTemplate = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Test.App" Publisher="CN=Test" Version="1.0.0.0"
                    ProcessorArchitecture="{0}" />
          <Properties>
            <DisplayName>Test App</DisplayName>
            <Logo>Assets\Logo.png</Logo>
          </Properties>
        </Package>
        """;

    /// <summary>
    /// Creates a temporary .msixbundle file containing inner .msix packages
    /// with the given architectures. Each inner .msix has a minimal manifest.
    /// </summary>
    private static string CreateTestBundle(params string[] architectures)
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.msixbundle");

        using (var bundleStream = File.Create(bundlePath))
        using (var bundleArchive = new ZipArchive(bundleStream, ZipArchiveMode.Create))
        {
            foreach (var arch in architectures)
            {
                var msixEntryName = $"TestApp_{arch}.msix";
                var msixEntry = bundleArchive.CreateEntry(msixEntryName);

                using var msixEntryStream = msixEntry.Open();
                using var innerArchive = new ZipArchive(msixEntryStream, ZipArchiveMode.Create);

                var manifestEntry = innerArchive.CreateEntry("AppxManifest.xml");
                using var manifestStream = manifestEntry.Open();
                using var writer = new StreamWriter(manifestStream);
                writer.Write(string.Format(MinimalManifestTemplate, arch));
            }
        }

        return bundlePath;
    }

    private static string CreateEmptyBundle()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.msixbundle");
        using (var stream = File.Create(bundlePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            // Empty bundle — no .msix entries
            var readme = archive.CreateEntry("readme.txt");
            using var w = new StreamWriter(readme.Open());
            w.Write("empty");
        }
        return bundlePath;
    }

    private static string CreateBundleWithNoManifest()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.msixbundle");
        using (var bundleStream = File.Create(bundlePath))
        using (var bundleArchive = new ZipArchive(bundleStream, ZipArchiveMode.Create))
        {
            var msixEntry = bundleArchive.CreateEntry("NoManifest.msix");
            using var msixEntryStream = msixEntry.Open();
            using var innerArchive = new ZipArchive(msixEntryStream, ZipArchiveMode.Create);
            // Inner archive has no AppxManifest.xml
            var dummy = innerArchive.CreateEntry("dummy.txt");
            using var w = new StreamWriter(dummy.Open());
            w.Write("not a manifest");
        }
        return bundlePath;
    }

    // ── IsBundleFile ──

    [Theory]
    [InlineData("app.msixbundle", true)]
    [InlineData("app.appxbundle", true)]
    [InlineData("APP.MSIXBUNDLE", true)]
    [InlineData("app.msix", false)]
    [InlineData("app.appx", false)]
    [InlineData("app.zip", false)]
    public void IsBundleFile_DetectsCorrectly(string path, bool expected)
    {
        Assert.Equal(expected, ManifestParserService.IsBundleFile(path));
    }

    // ── IsSupportedFile ──

    [Theory]
    [InlineData("app.msix", true)]
    [InlineData("app.appx", true)]
    [InlineData("app.msixbundle", true)]
    [InlineData("app.appxbundle", true)]
    [InlineData("app.zip", false)]
    [InlineData("app.exe", false)]
    public void IsSupportedFile_DetectsCorrectly(string path, bool expected)
    {
        Assert.Equal(expected, ManifestParserService.IsSupportedFile(path));
    }

    // ── ExtractFromBundle ──

    [Fact]
    public void ExtractFromBundle_MultiArch_ReturnsAllPackages()
    {
        var bundlePath = CreateTestBundle("x64", "arm64");
        try
        {
            var results = ManifestParserService.ExtractFromBundle(bundlePath);

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Info.Architecture == "x64");
            Assert.Contains(results, r => r.Info.Architecture == "arm64");
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }

    [Fact]
    public void ExtractFromBundle_SingleArch_ReturnsSinglePackage()
    {
        var bundlePath = CreateTestBundle("x64");
        try
        {
            var results = ManifestParserService.ExtractFromBundle(bundlePath);

            Assert.Single(results);
            Assert.Equal("x64", results[0].Info.Architecture);
            Assert.Equal("Test.App", results[0].Info.Name);
            Assert.Equal("Test App", results[0].Info.DisplayName);
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }

    [Fact]
    public void ExtractFromBundle_EmptyBundle_Throws()
    {
        var bundlePath = CreateEmptyBundle();
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => ManifestParserService.ExtractFromBundle(bundlePath));
            Assert.Contains("does not contain", ex.Message);
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }

    [Fact]
    public void ExtractFromBundle_NoValidManifests_Throws()
    {
        var bundlePath = CreateBundleWithNoManifest();
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => ManifestParserService.ExtractFromBundle(bundlePath));
            Assert.Contains("No valid", ex.Message);
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }

    [Fact]
    public void ExtractFromBundle_EntryNamesPreserved()
    {
        var bundlePath = CreateTestBundle("x64", "arm64");
        try
        {
            var results = ManifestParserService.ExtractFromBundle(bundlePath);

            Assert.Contains(results, r => r.EntryName == "TestApp_x64.msix");
            Assert.Contains(results, r => r.EntryName == "TestApp_arm64.msix");
            Assert.Contains(results, r => r.Label == "TestApp_x64.msix");
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }

    [Fact]
    public void ExtractFromBundle_ManifestsAreAnalyzable()
    {
        var bundlePath = CreateTestBundle("x64");
        try
        {
            var results = ManifestParserService.ExtractFromBundle(bundlePath);
            var findings = RulesEngine.Analyze(results[0].Manifest);

            // Should produce at least identity findings
            Assert.NotEmpty(findings);
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }
}
