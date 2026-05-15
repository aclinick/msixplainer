using System.Xml.Linq;

namespace MSIXplainer.Models;

/// <summary>
/// Result of extracting a single package from an .msixbundle or .appxbundle.
/// </summary>
public sealed class BundlePackageResult
{
    /// <summary>The filename of the inner .msix/.appx entry within the bundle.</summary>
    public required string EntryName { get; init; }

    public required XDocument Manifest { get; init; }
    public required string RawXml { get; init; }
    public required PackageInfo Info { get; init; }

    /// <summary>
    /// Short label derived from the entry name, e.g. "MSIXplainer_x64.msix".
    /// </summary>
    public string Label => Path.GetFileName(EntryName);
}
