namespace MSIXplainer.Models;

/// <summary>
/// One inner package entry from a bundle's AppxBundleManifest.xml.
/// </summary>
public sealed class BundleInnerPackage
{
    /// <summary>Path of the inner .msix/.appx within the bundle ZIP (e.g. "App_x64.msix").</summary>
    public required string FileName { get; init; }

    /// <summary>"application" or "resource".</summary>
    public required string Type { get; init; }

    public required string Version { get; init; }

    /// <summary>Architecture for application packages; "neutral" for resource packages.</summary>
    public required string Architecture { get; init; }

    /// <summary>ResourceId for resource packages; empty for application packages.</summary>
    public string ResourceId { get; init; } = string.Empty;

    /// <summary>Languages declared by the package's Resources element.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Scale qualifiers (e.g. "100", "200") declared by Resources.</summary>
    public IReadOnlyList<string> Scales { get; init; } = [];

    /// <summary>Size of the inner package as recorded in the bundle manifest.</summary>
    public long Size { get; init; }

    public bool IsApplication => string.Equals(Type, "application", StringComparison.OrdinalIgnoreCase);
    public bool IsResource => string.Equals(Type, "resource", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Stable matching key for pairing the same logical package across two bundles.
    /// Application packages key on architecture; resource packages key on
    /// ResourceId (which typically encodes the qualifier such as language or scale).
    /// </summary>
    public string MatchKey =>
        IsApplication
            ? $"app|{Architecture.ToLowerInvariant()}"
            : $"resource|{ResourceId.ToLowerInvariant()}";

    /// <summary>Short human-readable label, e.g. "x64", "resources.en-us", "scale-200".</summary>
    public string Label
    {
        get
        {
            if (IsApplication) return Architecture;
            if (!string.IsNullOrEmpty(ResourceId)) return $"resources.{ResourceId}";
            return FileName;
        }
    }
}
