using System.Runtime.Versioning;
using MSIXplainer.Models;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace MSIXplainer.Services;

/// <summary>
/// Enumerates MSIX/AppX packages installed for the current user via the WinRT
/// <see cref="PackageManager"/> API. Excludes framework, resource, optional, and bundle
/// packages — only main packages (what users mentally consider "apps").
/// </summary>
/// <remarks>
/// Windows-only. Callers must guard with <see cref="OperatingSystem.IsWindows"/>
/// or accept a <see cref="PlatformNotSupportedException"/>.
///
/// <para>
/// Two-pass loading: prefer <see cref="ListWithoutIcons"/> + per-row
/// <see cref="ResolveIcon"/> so the UI can render the list in &lt;1s and stream
/// icons in afterward. <see cref="List"/> remains for headless callers (CLI / tests)
/// that need everything eagerly.
/// </para>
/// </remarks>
public static class InstalledPackageService
{
    /// <summary>
    /// Returns installed main packages for the current user, sorted by display name,
    /// with icon bytes already resolved. Synchronous + eager — fine for CLI/tests,
    /// avoid on UI threads (use <see cref="ListWithoutIcons"/> instead).
    /// On non-Windows platforms returns an empty list (does not throw).
    /// </summary>
    public static IReadOnlyList<InstalledPackage> List()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<InstalledPackage>();

        return ListWindows(withIcons: true);
    }

    /// <summary>
    /// Fast enumeration with <see cref="InstalledPackage.IconBytes"/> left null.
    /// Returns in ~0.4s for a typical user (vs several seconds for <see cref="List"/>).
    /// Pair with <see cref="ResolveIcon"/> to populate icons lazily per row.
    /// </summary>
    public static IReadOnlyList<InstalledPackage> ListWithoutIcons()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<InstalledPackage>();

        return ListWindows(withIcons: false);
    }

    /// <summary>
    /// Returns a copy of <paramref name="package"/> with <see cref="InstalledPackage.IconBytes"/>
    /// populated (or unchanged null if the asset is missing / access-denied). Pure function —
    /// safe to call from any thread. Never throws on icon resolution failure.
    /// </summary>
    public static InstalledPackage ResolveIcon(InstalledPackage package)
    {
        if (package.IconBytes is { Length: > 0 }) return package;
        if (string.IsNullOrEmpty(package.InstallLocation)) return package;

        try
        {
            var bytes = ManifestParserService.TryGetIconFromInstallFolder(package.InstallLocation);
            return bytes is { Length: > 0 }
                ? package with { IconBytes = bytes }
                : package;
        }
        catch
        {
            return package;
        }
    }

    /// <summary>
    /// Finds an installed package by Package Family Name (case-insensitive).
    /// Returns <c>null</c> if not found or platform is not Windows.
    /// Includes icon bytes (uses <see cref="List"/>).
    /// </summary>
    public static InstalledPackage? FindByFamilyName(string packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
            return null;

        return List().FirstOrDefault(p =>
            string.Equals(p.PackageFamilyName, packageFamilyName, StringComparison.OrdinalIgnoreCase));
    }

    [SupportedOSPlatform("windows10.0.10240.0")]
    private static IReadOnlyList<InstalledPackage> ListWindows(bool withIcons)
    {
        var pm = new PackageManager();

        // Empty user SID == current user. PackageTypes.Main excludes Framework,
        // Resource, Optional, and Bundle packages in one call (no manual filtering).
        var packages = pm.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Main);

        var result = new List<InstalledPackage>();
        foreach (var pkg in packages)
        {
            var mapped = TryMap(pkg, withIcons);
            if (mapped is not null) result.Add(mapped);
        }

        return result
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Maps a WinRT <see cref="Package"/> to our DTO. Wraps every property access
    /// because the API can throw <see cref="System.IO.FileNotFoundException"/> or access-denied
    /// for system / partially-staged packages.
    /// </summary>
    [SupportedOSPlatform("windows10.0.10240.0")]
    internal static InstalledPackage? TryMap(Package pkg, bool withIcon = true)
    {
        try
        {
            var id = pkg.Id;
            if (id is null || string.IsNullOrEmpty(id.Name)) return null;

            // Package.DisplayName auto-resolves ms-resource:// indirection via the
            // package's MRT resource map. Fall back to identity Name when resolution
            // fails — observed failure modes include returning an empty string, a
            // leading "ms-resource:..." literal, OR a longer string containing an
            // unresolved "ms-resource:" fragment embedded mid-text.
            string displayName;
            try { displayName = pkg.DisplayName; }
            catch { displayName = string.Empty; }
            if (string.IsNullOrWhiteSpace(displayName)
                || displayName.Contains("ms-resource:", StringComparison.Ordinal))
            {
                displayName = id.Name;
            }

            string installLocation = string.Empty;
            try { installLocation = pkg.InstalledLocation?.Path ?? string.Empty; }
            catch { /* access denied / missing */ }

            var v = id.Version;
            var version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";

            return new InstalledPackage
            {
                Name = id.Name,
                DisplayName = displayName,
                PackageFamilyName = id.FamilyName ?? string.Empty,
                PackageFullName = id.FullName ?? string.Empty,
                Version = version,
                Publisher = id.Publisher ?? string.Empty,
                InstallLocation = installLocation,
                Architecture = id.Architecture.ToString(),
                IconBytes = withIcon
                    ? ManifestParserService.TryGetIconFromInstallFolder(installLocation)
                    : null
            };
        }
        catch
        {
            // Skip any package we can't read — never let one bad apple drop the list.
            return null;
        }
    }
}
