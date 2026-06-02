using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using MSIXplainer.Models;

namespace MSIXplainer.Services;

/// <summary>
/// Parses AppxBundleManifest.xml from a .msixbundle / .appxbundle, with the
/// same untrusted-input hardening as ManifestParserService.
/// </summary>
public static class BundleManifestParser
{
    private const string BundleManifestEntryName = "AppxMetadata/AppxBundleManifest.xml";
    private const long MaxBundleManifestBytes = 50 * 1024 * 1024;

    public static IReadOnlyList<BundleInnerPackage> ExtractFromBundle(string bundlePath)
    {
        using var archive = ZipFile.OpenRead(bundlePath);
        return ExtractFromArchive(archive);
    }

    public static IReadOnlyList<BundleInnerPackage> ExtractFromArchive(ZipArchive archive)
    {
        var entry = archive.GetEntry(BundleManifestEntryName)
            ?? throw new InvalidOperationException(
                $"Bundle does not contain {BundleManifestEntryName} — cannot determine inner package layout.");

        if (entry.Length > MaxBundleManifestBytes)
            throw new InvalidOperationException(
                $"{BundleManifestEntryName} exceeds {MaxBundleManifestBytes / (1024 * 1024)} MB — refusing to parse.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<BundleInnerPackage> Parse(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaxBundleManifestBytes
        };

        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        var doc = XDocument.Load(xmlReader);

        var root = doc.Root
            ?? throw new InvalidOperationException("AppxBundleManifest.xml has no root element.");

        // Stay namespace-tolerant — there are several bundle namespace versions in the wild.
        var packagesEl = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Packages")
            ?? throw new InvalidOperationException("AppxBundleManifest.xml has no <Packages> element.");

        var list = new List<BundleInnerPackage>();
        foreach (var p in packagesEl.Elements().Where(e => e.Name.LocalName == "Package"))
        {
            var fileName = p.Attribute("FileName")?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(fileName)) continue;

            var type = p.Attribute("Type")?.Value ?? "application";
            var version = p.Attribute("Version")?.Value ?? string.Empty;
            var arch = p.Attribute("Architecture")?.Value ?? "neutral";
            var resourceId = p.Attribute("ResourceId")?.Value ?? string.Empty;
            long.TryParse(p.Attribute("Size")?.Value, out var size);

            var resourcesEl = p.Elements().FirstOrDefault(e => e.Name.LocalName == "Resources");
            var languages = resourcesEl is null
                ? []
                : resourcesEl.Elements()
                    .Where(e => e.Name.LocalName == "Resource")
                    .Select(e => e.Attribute("Language")?.Value ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            var scales = resourcesEl is null
                ? []
                : resourcesEl.Elements()
                    .Where(e => e.Name.LocalName == "Resource")
                    .Select(e => e.Attribute("Scale")?.Value ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

            list.Add(new BundleInnerPackage
            {
                FileName = fileName,
                Type = type,
                Version = version,
                Architecture = arch,
                ResourceId = resourceId,
                Languages = languages,
                Scales = scales,
                Size = size
            });
        }

        return list;
    }
}
