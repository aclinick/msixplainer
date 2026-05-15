namespace MSIXplainer.Models;

/// <summary>
/// Represents a section in the NavigationView — either the overview,
/// a finding category, or the raw XML view.
/// </summary>
public sealed class ManifestSection
{
    public required string Tag { get; init; }
    public required string Label { get; init; }
    public required string IconGlyph { get; init; }
    public int FindingCount { get; init; }
    public FindingSeverity WorstSeverity { get; init; }
    /// <summary>Extracted app icon bytes for Application sections. Null uses FontIcon fallback.</summary>
    public byte[]? IconBytes { get; init; }

    public static string IconForCategory(FindingCategory category) => category switch
    {
        FindingCategory.Identity => "\uE77B",
        FindingCategory.Trust => "\uE72E",
        FindingCategory.Capabilities => "\uE8D7",
        FindingCategory.DeviceAccess => "\uE772",
        FindingCategory.NetworkAccess => "\uE774",
        FindingCategory.Startup => "\uE7E8",
        FindingCategory.Protocols => "\uE71B",
        FindingCategory.FileAssociations => "\uE8A5",
        FindingCategory.Virtualization => "\uE8F1",
        FindingCategory.COM => "\uE943",
        FindingCategory.BackgroundTasks => "\uE823",
        FindingCategory.OfficeIntegration => "\uE8A1",
        FindingCategory.WebView2 => "\uEB41",
        FindingCategory.VDI => "\uE770",
        FindingCategory.Services => "\uE912",
        _ => "\uE9CE"
    };
}
