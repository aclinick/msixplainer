namespace MSIXplainer.Models;

/// <summary>
/// A single MSIX/AppX package installed on the current Windows machine.
/// Returned by <see cref="Services.InstalledPackageService"/>.
/// </summary>
public sealed record InstalledPackage
{
    /// <summary>
    /// Package identity name (e.g. <c>Microsoft.WindowsCalculator</c>). Stable id, not user-facing.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Friendly display name resolved from the manifest (handles <c>ms-resource:</c> indirection
    /// via the WinRT <c>Package.DisplayName</c> projection). Falls back to <see cref="Name"/>
    /// if the package's resources can't be resolved.
    /// </summary>
    public required string DisplayName { get; init; }

    public required string PackageFamilyName { get; init; }
    public required string PackageFullName { get; init; }
    public required string Version { get; init; }
    public required string Publisher { get; init; }
    public required string InstallLocation { get; init; }
    public required string Architecture { get; init; }

    /// <summary>
    /// Square44x44 logo bytes (typically PNG) read from the package's install folder,
    /// or <c>null</c> when the asset is missing or access-denied (system packages
    /// under <c>C:\Program Files\WindowsApps</c> may refuse read access).
    /// </summary>
    public byte[]? IconBytes { get; init; }

    /// <summary>
    /// Path to <c>AppxManifest.xml</c> inside <see cref="InstallLocation"/>, or
    /// <c>null</c> if the install location is missing/inaccessible.
    /// </summary>
    public string? ManifestPath =>
        string.IsNullOrEmpty(InstallLocation)
            ? null
            : Path.Combine(InstallLocation, "AppxManifest.xml");
}
