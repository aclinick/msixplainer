using MSIXplainer.Models;
using MSIXplainer.Services;
using Xunit;

namespace MSIXplainer.Core.Tests;

public class InstalledPackageServiceTests
{
    [Fact]
    public void ManifestPath_BuildsFromInstallLocation()
    {
        var pkg = NewPackage(installLocation: Path.Combine("C:", "apps", "myapp"));
        Assert.Equal(
            Path.Combine("C:", "apps", "myapp", "AppxManifest.xml"),
            pkg.ManifestPath);
    }

    [Fact]
    public void ManifestPath_EmptyInstallLocation_IsNull()
    {
        Assert.Null(NewPackage(installLocation: "").ManifestPath);
    }

    [Fact]
    public void List_OnNonWindows_ReturnsEmpty()
    {
        if (OperatingSystem.IsWindows())
            return; // Only meaningful off-Windows; PackageManager is unavailable there.

        Assert.Empty(InstalledPackageService.List());
    }

    [Fact]
    public void List_OnWindows_ReturnsMainPackagesWithValidIdentity()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var packages = InstalledPackageService.List();

        // Don't assert specific packages — CI / dev images vary. Just verify the contract:
        // no throw, valid identity fields, every PFN is in Name_PublisherHash form, and
        // every row has a non-empty DisplayName (either resolved or fallen back to Name).
        Assert.NotNull(packages);
        foreach (var pkg in packages)
        {
            Assert.False(string.IsNullOrEmpty(pkg.Name), $"Name empty for {pkg.PackageFullName}");
            Assert.False(string.IsNullOrEmpty(pkg.DisplayName), $"DisplayName empty for {pkg.PackageFullName}");
            Assert.False(string.IsNullOrEmpty(pkg.PackageFamilyName), $"PFN empty for {pkg.Name}");
            Assert.Contains("_", pkg.PackageFamilyName);
            Assert.DoesNotContain("ms-resource:", pkg.DisplayName); // unresolved indirections must fall back
        }
    }

    [Fact]
    public void List_OnWindows_ReturnsSortedByDisplayName()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var packages = InstalledPackageService.List();
        if (packages.Count < 2) return;

        var displayNames = packages.Select(p => p.DisplayName).ToList();
        var sorted = displayNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, displayNames);
    }

    [Fact]
    public void FindByFamilyName_EmptyOrNull_ReturnsNull()
    {
        Assert.Null(InstalledPackageService.FindByFamilyName(""));
        Assert.Null(InstalledPackageService.FindByFamilyName("   "));
        Assert.Null(InstalledPackageService.FindByFamilyName(null!));
    }

    [Fact]
    public void FindByFamilyName_OnWindows_RoundTripsKnownPackage()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var first = InstalledPackageService.List().FirstOrDefault();
        if (first is null) return; // No packages on this machine — skip.

        var found = InstalledPackageService.FindByFamilyName(first.PackageFamilyName);
        Assert.NotNull(found);
        Assert.Equal(first.PackageFamilyName, found!.PackageFamilyName);
    }

    [Fact]
    public void FindByFamilyName_UnknownPackage_ReturnsNull()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Null(InstalledPackageService.FindByFamilyName(
            "MSIXplainer.NonExistent.Package_0000000000000"));
    }

    [Fact]
    public void ListWithoutIcons_OnNonWindows_ReturnsEmpty()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Empty(InstalledPackageService.ListWithoutIcons());
    }

    [Fact]
    public void ListWithoutIcons_OnWindows_AllRowsHaveNullIcons()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var packages = InstalledPackageService.ListWithoutIcons();
        Assert.NotNull(packages);
        // Fast path explicitly skips icon resolution — every row must come back with null bytes.
        Assert.All(packages, p => Assert.Null(p.IconBytes));
    }

    [Fact]
    public void ListWithoutIcons_OnWindows_ReturnsSameIdentitiesAsList()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var fast = InstalledPackageService.ListWithoutIcons()
            .Select(p => p.PackageFullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var full = InstalledPackageService.List()
            .Select(p => p.PackageFullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(full, fast);
    }

    [Fact]
    public void ResolveIcon_AlreadyHasIcon_ReturnsSameInstance()
    {
        var pkg = NewPackage(installLocation: "C:\\nope") with { IconBytes = [0x01, 0x02] };
        var resolved = InstalledPackageService.ResolveIcon(pkg);
        Assert.Same(pkg, resolved);
    }

    [Fact]
    public void ResolveIcon_EmptyInstallLocation_ReturnsSameInstance()
    {
        var pkg = NewPackage(installLocation: "");
        var resolved = InstalledPackageService.ResolveIcon(pkg);
        Assert.Same(pkg, resolved);
    }

    [Fact]
    public void ResolveIcon_NonExistentFolder_ReturnsSameInstanceWithNullBytes()
    {
        var pkg = NewPackage(installLocation: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var resolved = InstalledPackageService.ResolveIcon(pkg);
        Assert.Null(resolved.IconBytes);
    }

    [Fact]
    public void ResolveIcon_OnWindows_PopulatesIconForRealPackage()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Pick a package whose icon-with full-pass actually resolves, then verify
        // ResolveIcon arrives at the same bytes when started from the icon-less version.
        var withIcons = InstalledPackageService.List();
        var sample = withIcons.FirstOrDefault(p => p.IconBytes is { Length: > 0 });
        if (sample is null) return; // No package on this box exposed an icon — skip.

        var stripped = sample with { IconBytes = null };
        var resolved = InstalledPackageService.ResolveIcon(stripped);

        Assert.NotNull(resolved.IconBytes);
        Assert.Equal(sample.IconBytes!.Length, resolved.IconBytes!.Length);
    }

    private static InstalledPackage NewPackage(string installLocation) => new()
    {
        Name = "X",
        DisplayName = "X",
        PackageFamilyName = "X_abc",
        PackageFullName = "X_1.0.0.0_x64__abc",
        Version = "1.0.0.0",
        Publisher = "CN=X",
        InstallLocation = installLocation,
        Architecture = "X64"
    };
}
