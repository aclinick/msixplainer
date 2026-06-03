using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MSIXplainer.Models;
using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class UpdateDiffBundleTests
{
    private const long BlockSize = 64 * 1024;
    private static readonly XNamespace BmNs = "http://schemas.microsoft.com/appx/2010/blockmap";
    private static readonly XNamespace BundleNs = "http://schemas.microsoft.com/appx/2013/bundle";

    private static readonly string MinimalManifestTemplate = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Test.App" Publisher="CN=Test" Version="{0}" ProcessorArchitecture="{1}" />
          <Properties>
            <DisplayName>TestApp</DisplayName>
            <PublisherDisplayName>Test</PublisherDisplayName>
            <Logo>Assets\Logo.png</Logo>
          </Properties>
        </Package>
        """;

    private sealed record SyntheticFile(string Path, byte[] Content);

    private sealed record InnerSpec(string FileName, string Type, string Architecture,
        string ResourceId, string Version, SyntheticFile[] Files);

    /// <summary>
    /// Builds a synthetic .msixbundle with an AppxBundleManifest.xml plus inner
    /// .msix packages, each containing a manifest, the given payload files, and
    /// a generated AppxBlockMap.xml.
    /// </summary>
    private static string CreateBundle(params InnerSpec[] inners)
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"udb_{Guid.NewGuid()}.msixbundle");

        using var fs = File.Create(bundlePath);
        using var bundleArchive = new ZipArchive(fs, ZipArchiveMode.Create);

        // Build each inner .msix to a MemoryStream, then write into the bundle ZIP.
        var innerStreams = new List<(InnerSpec spec, long size)>();
        foreach (var inner in inners)
        {
            var innerBytes = BuildInnerPackage(inner);
            var entry = bundleArchive.CreateEntry(inner.FileName, CompressionLevel.NoCompression);
            using (var s = entry.Open())
            {
                s.Write(innerBytes, 0, innerBytes.Length);
            }
            innerStreams.Add((inner, innerBytes.Length));
        }

        // Bundle manifest
        var packagesEl = new XElement(BundleNs + "Packages");
        foreach (var (spec, size) in innerStreams)
        {
            var pkgEl = new XElement(BundleNs + "Package",
                new XAttribute("Type", spec.Type),
                new XAttribute("Version", spec.Version),
                new XAttribute("FileName", spec.FileName),
                new XAttribute("Size", size),
                new XAttribute("Offset", 0));
            if (spec.Type.Equals("application", StringComparison.OrdinalIgnoreCase))
                pkgEl.Add(new XAttribute("Architecture", spec.Architecture));
            if (!string.IsNullOrEmpty(spec.ResourceId))
                pkgEl.Add(new XAttribute("ResourceId", spec.ResourceId));
            packagesEl.Add(pkgEl);
        }

        var bundleDoc = new XDocument(
            new XElement(BundleNs + "Bundle",
                new XElement(BundleNs + "Identity",
                    new XAttribute("Name", "Test.App"),
                    new XAttribute("Publisher", "CN=Test"),
                    new XAttribute("Version", "1.0.0.0")),
                packagesEl));

        var bmEntry = bundleArchive.CreateEntry("AppxMetadata/AppxBundleManifest.xml", CompressionLevel.Optimal);
        using (var bms = bmEntry.Open())
        {
            bundleDoc.Save(bms);
        }

        return bundlePath;
    }

    private static byte[] BuildInnerPackage(InnerSpec spec)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Manifest
            var manifestEntry = archive.CreateEntry("AppxManifest.xml", CompressionLevel.Optimal);
            using (var s = manifestEntry.Open())
            using (var sw = new StreamWriter(s, Encoding.UTF8))
            {
                sw.Write(string.Format(MinimalManifestTemplate, spec.Version, spec.Architecture));
            }

            var blockMap = new XElement(BmNs + "BlockMap",
                new XAttribute("HashMethod", "http://www.w3.org/2001/04/xmlenc#sha256"));

            foreach (var file in spec.Files)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                using (var s = entry.Open()) { s.Write(file.Content, 0, file.Content.Length); }

                var fileEl = new XElement(BmNs + "File",
                    new XAttribute("Name", file.Path.Replace('/', '\\')),
                    new XAttribute("Size", file.Content.Length),
                    new XAttribute("LfhSize", 30 + file.Path.Length));

                int offset = 0;
                while (offset < file.Content.Length || (file.Content.Length == 0 && offset == 0))
                {
                    var len = (int)Math.Min(BlockSize, file.Content.Length - offset);
                    var block = new byte[len];
                    Array.Copy(file.Content, offset, block, 0, len);
                    var hash = Convert.ToBase64String(SHA256.HashData(block));
                    fileEl.Add(new XElement(BmNs + "Block", new XAttribute("Hash", hash)));
                    offset += len;
                    if (len == 0) break;
                }

                blockMap.Add(fileEl);
            }

            var bmEntry = archive.CreateEntry("AppxBlockMap.xml", CompressionLevel.Optimal);
            using (var bms = bmEntry.Open())
            {
                new XDocument(blockMap).Save(bms);
            }
        }
        return ms.ToArray();
    }

    private static byte[] Bytes(string seed, int length)
    {
        var rng = new Random(seed.GetHashCode());
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }

    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CompareBundles_MatchesArchitectures_AndAggregates()
    {
        var oldBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.0.0.0",
                [new SyntheticFile("App.exe", Bytes("x64-v1", 200_000))]),
            new InnerSpec("App_arm64.msix", "application", "arm64", "", "1.0.0.0",
                [new SyntheticFile("App.exe", Bytes("arm64-v1", 200_000))]));

        var newBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.1.0.0",
                [new SyntheticFile("App.exe", Bytes("x64-v2", 200_000))]),
            new InnerSpec("App_arm64.msix", "application", "arm64", "", "1.1.0.0",
                [new SyntheticFile("App.exe", Bytes("arm64-v2", 200_000))]));

        try
        {
            var result = UpdateDiffService.CompareBundles(oldBundle, newBundle);

            Assert.Equal(2, result.PackageDiffs.Count);
            Assert.Contains(result.PackageDiffs, p => p.Architecture == "x64");
            Assert.Contains(result.PackageDiffs, p => p.Architecture == "arm64");
            Assert.Empty(result.AddedPackages);
            Assert.Empty(result.RemovedPackages);
            Assert.True(result.TotalDeltaDownloadBytes > 0);
        }
        finally
        {
            File.Delete(oldBundle);
            File.Delete(newBundle);
        }
    }

    [Fact]
    public void CompareBundles_FlagsAddedArchitecture()
    {
        var oldBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.0.0.0",
                [new SyntheticFile("App.exe", Bytes("v1", 1000))]));
        var newBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.1.0.0",
                [new SyntheticFile("App.exe", Bytes("v2", 1000))]),
            new InnerSpec("App_arm64.msix", "application", "arm64", "", "1.1.0.0",
                [new SyntheticFile("App.exe", Bytes("arm64", 1000))]));

        try
        {
            var result = UpdateDiffService.CompareBundles(oldBundle, newBundle);

            Assert.Single(result.PackageDiffs); // only x64 has a pair
            Assert.Contains("arm64", result.AddedPackages);
            Assert.Empty(result.RemovedPackages);
            Assert.Contains(result.Warnings, w => w.Contains("adds these architectures"));
        }
        finally
        {
            File.Delete(oldBundle);
            File.Delete(newBundle);
        }
    }

    [Fact]
    public void CompareBundles_FlagsDroppedArchitecture()
    {
        var oldBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.0.0.0",
                [new SyntheticFile("App.exe", Bytes("v1", 1000))]),
            new InnerSpec("App_arm64.msix", "application", "arm64", "", "1.0.0.0",
                [new SyntheticFile("App.exe", Bytes("a1", 1000))]));
        var newBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.1.0.0",
                [new SyntheticFile("App.exe", Bytes("v2", 1000))]));

        try
        {
            var result = UpdateDiffService.CompareBundles(oldBundle, newBundle);

            Assert.Single(result.PackageDiffs);
            Assert.Contains("arm64", result.RemovedPackages);
            Assert.Contains(result.Warnings, w => w.Contains("no longer ships"));
        }
        finally
        {
            File.Delete(oldBundle);
            File.Delete(newBundle);
        }
    }

    [Fact]
    public void CompareBundles_MatchesResourcePackagesByResourceId()
    {
        var oldBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.0.0.0",
                [new SyntheticFile("App.exe", Bytes("v1", 1000))]),
            new InnerSpec("App_en.msix", "resource", "neutral", "en-us", "1.0.0.0",
                [new SyntheticFile("strings.pri", Bytes("en-v1", 5_000))]),
            new InnerSpec("App_fr.msix", "resource", "neutral", "fr-fr", "1.0.0.0",
                [new SyntheticFile("strings.pri", Bytes("fr-v1", 5_000))]));

        var newBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.1.0.0",
                [new SyntheticFile("App.exe", Bytes("v2", 1000))]),
            new InnerSpec("App_en.msix", "resource", "neutral", "en-us", "1.1.0.0",
                [new SyntheticFile("strings.pri", Bytes("en-v1", 5_000))]), // unchanged
            new InnerSpec("App_fr.msix", "resource", "neutral", "fr-fr", "1.1.0.0",
                [new SyntheticFile("strings.pri", Bytes("fr-v2", 5_000))])); // changed

        try
        {
            var result = UpdateDiffService.CompareBundles(oldBundle, newBundle);

            Assert.Equal(3, result.PackageDiffs.Count);

            var enDiff = result.PackageDiffs.Single(p => p.Label.Contains("en-us"));
            // SDK-parity: unchanged content => zero block-delta. Overhead reported separately.
            Assert.Equal(0, enDiff.DeltaDownloadBytes);
            Assert.True(enDiff.OverheadBytes > 0);

            var frDiff = result.PackageDiffs.Single(p => p.Label.Contains("fr-fr"));
            Assert.True(frDiff.DeltaDownloadBytes > 0,
                "Changed resource pack should have positive block-delta.");
        }
        finally
        {
            File.Delete(oldBundle);
            File.Delete(newBundle);
        }
    }

    [Fact]
    public void CompareBundles_RejectsSinglePackageInputs()
    {
        var fakeMsix = Path.Combine(Path.GetTempPath(), $"udb_{Guid.NewGuid()}.msix");
        File.WriteAllText(fakeMsix, "");
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => UpdateDiffService.CompareBundles(fakeMsix, fakeMsix));
        }
        finally
        {
            File.Delete(fakeMsix);
        }
    }

    [Fact]
    public void BundleManifestParser_ReadsTypeArchAndResourceId()
    {
        var bundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.0.0.0",
                [new SyntheticFile("App.exe", Bytes("a", 100))]),
            new InnerSpec("App_en.msix", "resource", "neutral", "en-us", "1.0.0.0",
                [new SyntheticFile("strings.pri", Bytes("b", 100))]));

        try
        {
            var inners = BundleManifestParser.ExtractFromBundle(bundle);
            Assert.Equal(2, inners.Count);

            var app = inners.Single(i => i.IsApplication);
            Assert.Equal("x64", app.Architecture);
            Assert.Equal("app|x64|||", app.MatchKey);

            var res = inners.Single(i => i.IsResource);
            Assert.Equal("en-us", res.ResourceId);
            Assert.Equal("resource|neutral|en-us||", res.MatchKey);
        }
        finally
        {
            File.Delete(bundle);
        }
    }

    [Fact]
    public void CompareBundles_HandlesMultipleInnersWithSameArchitecture()
    {
        // Some real-world bundles (e.g. Microsoft.DesktopAppInstaller) ship more
        // than one application-type inner package for the same architecture
        // (main app + companion/asset partitions). Earlier versions of the diff
        // service used `app|{arch}` as the key and crashed with
        // "An item with the same key has already been added. Key: app|x64".
        // This regression test pins the new behavior: we no longer throw.
        var oldBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.0.0.0",
                [new SyntheticFile("App.exe", Bytes("a", 100))]),
            new InnerSpec("Extras_x64.msix", "application", "x64", "extras", "1.0.0.0",
                [new SyntheticFile("Extras.dll", Bytes("b", 100))]));
        var newBundle = CreateBundle(
            new InnerSpec("App_x64.msix", "application", "x64", "", "1.1.0.0",
                [new SyntheticFile("App.exe", Bytes("a", 100))]),
            new InnerSpec("Extras_x64.msix", "application", "x64", "extras", "1.1.0.0",
                [new SyntheticFile("Extras.dll", Bytes("b", 100))]));

        try
        {
            var result = UpdateDiffService.CompareBundles(oldBundle, newBundle);
            Assert.Equal(2, result.PackageDiffs.Count);
        }
        finally
        {
            File.Delete(oldBundle);
            File.Delete(newBundle);
        }
    }
}
