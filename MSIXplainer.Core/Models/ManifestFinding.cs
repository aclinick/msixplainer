namespace MSIXplainer.Models;

public enum FindingSeverity
{
    Info,
    Review,
    Warning,
    Critical
}

public enum FindingCategory
{
    Identity,
    Trust,
    Capabilities,
    DeviceAccess,
    NetworkAccess,
    Startup,
    Protocols,
    FileAssociations,
    Virtualization,
    COM,
    BackgroundTasks,
    OfficeIntegration,
    WebView2,
    VDI,
    Services,
    Other
}

public sealed class ManifestFinding
{
    public string RuleId { get; init; } = string.Empty;
    public required FindingCategory Category { get; init; }
    public required FindingSeverity Severity { get; set; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string WhyItMatters { get; init; }
    public required string Recommendation { get; init; }
    public string XmlSnippet { get; init; } = string.Empty;

    public string SeverityLabel => Severity switch
    {
        FindingSeverity.Critical => "Critical",
        FindingSeverity.Warning => "Warning",
        FindingSeverity.Review => "Review",
        _ => "Info"
    };

    public string CategoryLabel => Category switch
    {
        FindingCategory.Identity => "Identity",
        FindingCategory.Trust => "Trust Level",
        FindingCategory.Capabilities => "Capabilities",
        FindingCategory.DeviceAccess => "Device Access",
        FindingCategory.NetworkAccess => "Network Access",
        FindingCategory.Startup => "Startup",
        FindingCategory.Protocols => "Protocols",
        FindingCategory.FileAssociations => "File Associations",
        FindingCategory.Virtualization => "Virtualization",
        FindingCategory.COM => "COM Registration",
        FindingCategory.BackgroundTasks => "Background Tasks",
        FindingCategory.OfficeIntegration => "Office Integration",
        FindingCategory.WebView2 => "WebView2",
        FindingCategory.VDI => "VDI / Deployment",
        FindingCategory.Services => "Services",
        _ => "Other"
    };

    public string SeverityIcon => Severity switch
    {
        FindingSeverity.Critical => "\uEA39",
        FindingSeverity.Warning => "\uE7BA",
        FindingSeverity.Review => "\uE9CE",
        _ => "\uE946"
    };
}
