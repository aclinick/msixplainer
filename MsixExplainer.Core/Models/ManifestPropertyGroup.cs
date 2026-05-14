namespace MsixExplorer.Models;

/// <summary>
/// A group of related manifest properties displayed together with a header.
/// Each section page contains one or more groups (e.g., "Entry Point", "Visual Elements").
/// </summary>
public sealed class ManifestPropertyGroup
{
    public required string Header { get; init; }
    public string Description { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = "\uE9CE";
    public List<ManifestProperty> Properties { get; init; } = [];

    public FindingSeverity WorstSeverity => Properties
        .Where(p => p.HasFinding)
        .Select(p => p.FindingSeverity)
        .DefaultIfEmpty(FindingSeverity.Info)
        .Max();

    public bool HasFindings => Properties.Any(p => p.HasFinding);
}

/// <summary>
/// A single manifest property row: label, value, explanation, and optional linked finding.
/// </summary>
public sealed class ManifestProperty
{
    public required string Label { get; init; }
    public string Value { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public ManifestFinding? Finding { get; set; }

    public bool HasFinding => Finding is not null;
    public FindingSeverity FindingSeverity => Finding?.Severity ?? FindingSeverity.Info;
    public string FindingSeverityLabel => Finding?.SeverityLabel ?? "";
    public string FindingSeverityIcon => Finding?.SeverityIcon ?? "";
}
