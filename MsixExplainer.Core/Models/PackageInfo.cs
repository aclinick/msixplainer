namespace MsixExplorer.Models;

public sealed class PackageInfo
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string PublisherDisplayName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string MinOsVersion { get; init; } = string.Empty;
    public string MaxOsVersionTested { get; init; } = string.Empty;
    public string FrameworkDependencies { get; init; } = string.Empty;

    /// <summary>App icon bytes extracted from the package (Square44x44Logo). Null for sample manifests.</summary>
    public byte[]? AppIconBytes { get; set; }

    public int CriticalCount { get; set; }
    public int WarningCount { get; set; }
    public int ReviewCount { get; set; }
    public int InfoCount { get; set; }
    public int TotalFindings => CriticalCount + WarningCount + ReviewCount + InfoCount;

    public string SummaryLine =>
        $"{DisplayName} • {Version} • {Architecture}";

    public string PublisherLine =>
        string.IsNullOrEmpty(PublisherDisplayName)
            ? Publisher
            : $"{PublisherDisplayName} ({Publisher})";

    public string FindingsOverview =>
        $"{CriticalCount} critical · {WarningCount} warning · {ReviewCount} review · {InfoCount} info";
}
