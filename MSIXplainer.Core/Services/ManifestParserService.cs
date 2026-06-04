using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using MSIXplainer.Models;

namespace MSIXplainer.Services;

public static class ManifestParserService
{
    private static readonly XNamespace Ns =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    private static readonly string[] BundleExtensions = [".msixbundle", ".appxbundle"];
    private static readonly string[] PackageExtensions = [".msix", ".appx"];

    /// <summary>
    /// Returns true if the file extension indicates a bundle (ZIP of MSIX packages).
    /// </summary>
    public static bool IsBundleFile(string filePath)
        => BundleExtensions.Any(ext =>
            filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns true if the file extension indicates a single package or a bundle.
    /// </summary>
    public static bool IsSupportedFile(string filePath)
        => PackageExtensions.Concat(BundleExtensions)
            .Any(ext => filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Extracts AppxManifest.xml from an MSIX/APPX package (ZIP archive).
    /// Treats the package as untrusted: no code execution, no DTD processing.
    /// </summary>
    public static (XDocument Manifest, string RawXml, PackageInfo Info) ExtractFromPackage(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        return ExtractFromArchive(archive);
    }

    /// <summary>
    /// Extracts manifests from all inner MSIX/APPX packages in a bundle.
    /// Returns one result per architecture found in the bundle.
    /// </summary>
    public static List<BundlePackageResult> ExtractFromBundle(string filePath)
    {
        var results = new List<BundlePackageResult>();

        using var bundleArchive = ZipFile.OpenRead(filePath);
        var msixEntries = bundleArchive.Entries
            .Where(e => PackageExtensions.Any(ext =>
                e.FullName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.FullName)
            .ToList();

        if (msixEntries.Count == 0)
            throw new InvalidOperationException(
                "This bundle does not contain any MSIX or APPX packages.");

        foreach (var msixEntry in msixEntries)
        {
            // Guard: skip unreasonably large inner packages (500 MB)
            if (msixEntry.Length > 500 * 1024 * 1024)
                continue;

            using var msixStream = msixEntry.Open();
            using var memoryStream = new MemoryStream();
            msixStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var innerArchive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

            try
            {
                var (manifest, rawXml, info) = ExtractFromArchive(innerArchive);
                results.Add(new BundlePackageResult
                {
                    EntryName = msixEntry.FullName,
                    Manifest = manifest,
                    RawXml = rawXml,
                    Info = info
                });
            }
            catch (InvalidOperationException)
            {
                // Skip entries without a valid manifest (e.g., resource packages)
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                "No valid MSIX/APPX packages with manifests found in this bundle.");

        return results;
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

    /// <summary>
    /// Parses a loose <c>AppxManifest.xml</c> file from disk — used for analyzing
    /// already-installed packages where Windows has expanded the .msix to a
    /// folder under <c>%ProgramFiles%\WindowsApps</c>. Resolves the app icon
    /// from the same directory when possible.
    /// </summary>
    public static (XDocument Manifest, string RawXml, PackageInfo Info) ExtractFromManifestFile(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("AppxManifest.xml not found.", manifestPath);

        var fileInfo = new FileInfo(manifestPath);
        if (fileInfo.Length > 10 * 1024 * 1024)
            throw new InvalidOperationException(
                "AppxManifest.xml exceeds 10 MB — this is abnormal for a package manifest.");

        var rawXml = File.ReadAllText(manifestPath);
        var doc = ParseXmlSafely(rawXml);
        var info = ExtractPackageInfo(doc);
        info.AppIconBytes = TryExtractAppIconFromFolder(Path.GetDirectoryName(manifestPath)!, doc);
        return (doc, rawXml, info);
    }

    private static (XDocument Manifest, string RawXml, PackageInfo Info) ExtractFromArchive(ZipArchive archive)
    {
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

        var name = identity?.Attribute("Name")?.Value ?? "Unknown";
        var publisher = identity?.Attribute("Publisher")?.Value ?? "Unknown";
        var version = identity?.Attribute("Version")?.Value ?? "0.0.0.0";
        var architecture = identity?.Attribute("ProcessorArchitecture")?.Value ?? "neutral";
        var resourceId = identity?.Attribute("ResourceId")?.Value ?? "";

        return new PackageInfo
        {
            Name = name,
            Publisher = publisher,
            Version = version,
            Architecture = architecture,
            ResourceId = resourceId,
            DisplayName = properties?.Element(Ns + "DisplayName")?.Value ?? "Unknown",
            PublisherDisplayName = properties?.Element(Ns + "PublisherDisplayName")?.Value ?? "",
            Description = properties?.Element(Ns + "Description")?.Value ?? "",
            MinOsVersion = targetFamily?.Attribute("MinVersion")?.Value ?? "",
            MaxOsVersionTested = targetFamily?.Attribute("MaxVersionTested")?.Value ?? "",
            FrameworkDependencies = frameworks.Count > 0 ? string.Join(", ", frameworks) : "None",
            PackageFamilyName = PackageIdentityCalculator.ComputePackageFamilyName(name, publisher),
            PackageFullName = PackageIdentityCalculator.ComputePackageFullName(name, version, architecture, resourceId, publisher)
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

    /// <summary>
    /// Attempts to read the Square44x44 app icon bytes from an installed package's
    /// extracted folder. Returns <c>null</c> if the manifest can't be read, no icon
    /// is referenced, or file access is denied (common under <c>WindowsApps</c>).
    /// </summary>
    public static byte[]? TryGetIconFromInstallFolder(string installFolder)
    {
        if (string.IsNullOrEmpty(installFolder)) return null;

        var manifestPath = Path.Combine(installFolder, "AppxManifest.xml");
        if (!File.Exists(manifestPath)) return null;

        try
        {
            var doc = ParseXmlSafely(File.ReadAllText(manifestPath));
            return TryExtractAppIconFromFolder(installFolder, doc);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? TryExtractAppIconFromFolder(string folder, XDocument doc)
    {
        var app = doc.Root!.Element(Ns + "Applications")?.Element(Ns + "Application");
        if (app is null) return null;

        var ve = app.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
        var iconPath = ve?.Attribute("Square44x44Logo")?.Value;
        if (string.IsNullOrEmpty(iconPath)) return null;

        return TryReadIconFile(folder, iconPath);
    }

    private static byte[]? TryReadIconFile(string folder, string relativePath)
    {
        // Manifest paths use Windows-style separators; normalise for cross-platform.
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                     .Replace('\\', Path.DirectorySeparatorChar);

        var candidates = new List<string> { Path.Combine(folder, normalized) };

        // Try scale variants — Windows expands install location to include scaled assets.
        var dir = Path.GetDirectoryName(normalized) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(normalized);
        var ext = Path.GetExtension(normalized);
        foreach (var scale in new[] { "scale-200", "scale-150", "scale-125", "scale-100", "scale-400" })
        {
            var scaled = string.IsNullOrEmpty(dir)
                ? $"{baseName}.{scale}{ext}"
                : Path.Combine(dir, $"{baseName}.{scale}{ext}");
            candidates.Add(Path.Combine(folder, scaled));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var info = new FileInfo(candidate);
                if (info.Length > 1024 * 1024) continue;
                return File.ReadAllBytes(candidate);
            }
            catch
            {
                // WindowsApps may deny read access; skip and try next candidate.
            }
        }

        return null;
    }
}
