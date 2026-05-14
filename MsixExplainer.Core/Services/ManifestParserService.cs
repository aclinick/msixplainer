using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using MsixExplorer.Models;

namespace MsixExplorer.Services;

public static class ManifestParserService
{
    private static readonly XNamespace Ns =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    /// <summary>
    /// Extracts AppxManifest.xml from an MSIX/APPX package (ZIP archive).
    /// Treats the package as untrusted: no code execution, no DTD processing.
    /// </summary>
    public static (XDocument Manifest, string RawXml, PackageInfo Info) ExtractFromPackage(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var entry = archive.GetEntry("AppxManifest.xml")
            ?? throw new InvalidOperationException(
                "This file does not contain an AppxManifest.xml. Ensure it is a valid MSIX or APPX package.");

        // Guard against zip bombs
        if (entry.Length > 10 * 1024 * 1024)
            throw new InvalidOperationException(
                "AppxManifest.xml exceeds 10 MB — this is abnormal for a package manifest.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var rawXml = reader.ReadToEnd();

        var doc = ParseXmlSafely(rawXml);
        var info = ExtractPackageInfo(doc);
        info.AppIconBytes = TryExtractAppIcon(archive, doc);
        return (doc, rawXml, info);
    }

    /// <summary>
    /// Parses raw manifest XML (used for sample/testing).
    /// </summary>
    public static (XDocument Manifest, string RawXml, PackageInfo Info) ParseRawXml(string xml)
    {
        var doc = ParseXmlSafely(xml);
        var info = ExtractPackageInfo(doc);
        return (doc, xml, info);
    }

    private static XDocument ParseXmlSafely(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 10 * 1024 * 1024
        };

        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader);
    }

    private static PackageInfo ExtractPackageInfo(XDocument doc)
    {
        var root = doc.Root!;
        var identity = root.Element(Ns + "Identity");
        var properties = root.Element(Ns + "Properties");
        var deps = root.Element(Ns + "Dependencies");

        var desktopFamily = deps?.Elements()
            .FirstOrDefault(e => e.Attribute("Name")?.Value == "Windows.Desktop");
        var universalFamily = deps?.Elements()
            .FirstOrDefault(e => e.Attribute("Name")?.Value == "Windows.Universal");
        var targetFamily = desktopFamily ?? universalFamily ?? deps?.Elements().FirstOrDefault();

        var frameworks = deps?.Elements()
            .Where(e => e.Name.LocalName == "PackageDependency")
            .Select(e => $"{e.Attribute("Name")?.Value} {e.Attribute("MinVersion")?.Value}")
            .ToList() ?? [];

        return new PackageInfo
        {
            Name = identity?.Attribute("Name")?.Value ?? "Unknown",
            Publisher = identity?.Attribute("Publisher")?.Value ?? "Unknown",
            Version = identity?.Attribute("Version")?.Value ?? "0.0.0.0",
            Architecture = identity?.Attribute("ProcessorArchitecture")?.Value ?? "neutral",
            DisplayName = properties?.Element(Ns + "DisplayName")?.Value ?? "Unknown",
            PublisherDisplayName = properties?.Element(Ns + "PublisherDisplayName")?.Value ?? "",
            Description = properties?.Element(Ns + "Description")?.Value ?? "",
            MinOsVersion = targetFamily?.Attribute("MinVersion")?.Value ?? "",
            MaxOsVersionTested = targetFamily?.Attribute("MaxVersionTested")?.Value ?? "",
            FrameworkDependencies = frameworks.Count > 0 ? string.Join(", ", frameworks) : "None"
        };
    }

    /// <summary>
    /// Extracts the app's Square44x44Logo from the ZIP archive.
    /// Tries the exact path first, then scale variants (scale-100, -125, -150, -200).
    /// </summary>
    private static byte[]? TryExtractAppIcon(ZipArchive archive, XDocument doc)
    {
        var app = doc.Root!.Element(Ns + "Applications")?.Element(Ns + "Application");
        if (app is null) return null;

        var ve = app.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
        var iconPath = ve?.Attribute("Square44x44Logo")?.Value;
        if (string.IsNullOrEmpty(iconPath)) return null;

        return TryExtractFile(archive, iconPath);
    }

    private static byte[]? TryExtractFile(ZipArchive archive, string path)
    {
        var normalized = path.Replace('\\', '/');
        var entry = archive.GetEntry(normalized);

        // Try scale variants if exact match not found
        if (entry is null)
        {
            var dir = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? "";
            var baseName = Path.GetFileNameWithoutExtension(normalized);
            var ext = Path.GetExtension(normalized);

            foreach (var scale in new[] { "scale-100", "scale-125", "scale-150", "scale-200", "scale-400" })
            {
                var scaledPath = string.IsNullOrEmpty(dir)
                    ? $"{baseName}.{scale}{ext}"
                    : $"{dir}/{baseName}.{scale}{ext}";
                entry = archive.GetEntry(scaledPath);
                if (entry is not null) break;
            }
        }

        if (entry is null || entry.Length > 1024 * 1024) return null;

        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
