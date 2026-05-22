using System.Text.Json;
using MSIXplainer.Models;

namespace MSIXplainer.Services;

/// <summary>
/// Per-rule severity overrides loaded from one or more JSON files.
/// Rule text (Title/Description/WhyItMatters/Recommendation) is intentionally
/// not user-editable — only the Severity is.
/// </summary>
public sealed class RuleSeverityOverrides
{
    private readonly Dictionary<string, FindingSeverity> _overrides;

    /// <summary>Sources that contributed each override (rule ID -> origin path or label).</summary>
    public IReadOnlyDictionary<string, string> Sources { get; }

    public IReadOnlyDictionary<string, FindingSeverity> Effective => _overrides;

    public static RuleSeverityOverrides Empty { get; } = new(
        new Dictionary<string, FindingSeverity>(),
        new Dictionary<string, string>());

    private RuleSeverityOverrides(
        Dictionary<string, FindingSeverity> overrides,
        Dictionary<string, string> sources)
    {
        _overrides = overrides;
        Sources = sources;
    }

    public FindingSeverity Resolve(string ruleId, FindingSeverity defaultSeverity)
        => _overrides.TryGetValue(ruleId, out var s) ? s : defaultSeverity;

    /// <summary>
    /// Loads overrides from a JSON file shaped like { "rule.id": "Info", ... }.
    /// Returns <see cref="Empty"/> if the file does not exist.
    /// Calls <paramref name="warn"/> for unknown rule IDs (when a catalog is supplied)
    /// or unparseable severity values; the offending entry is skipped.
    /// </summary>
    public static RuleSeverityOverrides LoadFromFile(
        string path,
        IReadOnlyCollection<string>? knownRuleIds = null,
        Action<string>? warn = null)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        var json = File.ReadAllText(path);
        return Parse(json, path, knownRuleIds, warn);
    }

    /// <summary>
    /// Parses overrides from a JSON string with the same shape as
    /// <see cref="LoadFromFile"/>. <paramref name="sourceLabel"/> is used as the
    /// origin tag for diagnostics.
    /// </summary>
    public static RuleSeverityOverrides Parse(
        string json,
        string sourceLabel,
        IReadOnlyCollection<string>? knownRuleIds = null,
        Action<string>? warn = null)
    {
        var overrides = new Dictionary<string, FindingSeverity>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            warn?.Invoke($"[{sourceLabel}] rules file must be a JSON object of rule IDs to severities.");
            return new RuleSeverityOverrides(overrides, sources);
        }

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var ruleId = prop.Name;
            var value = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString()
                : prop.Value.ToString();

            if (string.IsNullOrWhiteSpace(value))
            {
                warn?.Invoke($"[{sourceLabel}] rule \"{ruleId}\" has empty severity; skipping.");
                continue;
            }

            if (!Enum.TryParse<FindingSeverity>(value, ignoreCase: true, out var severity))
            {
                warn?.Invoke(
                    $"[{sourceLabel}] rule \"{ruleId}\" has unknown severity \"{value}\". " +
                    "Expected one of: Info, Review, Warning, Critical. Skipping.");
                continue;
            }

            if (knownRuleIds is not null && !IsKnownRuleId(ruleId, knownRuleIds))
            {
                warn?.Invoke(
                    $"[{sourceLabel}] rule ID \"{ruleId}\" is not recognized. " +
                    "It will be ignored. Run `msixplainer rules list` to see available IDs.");
                continue;
            }

            overrides[ruleId] = severity;
            sources[ruleId] = sourceLabel;
        }

        return new RuleSeverityOverrides(overrides, sources);
    }

    /// <summary>
    /// Merges multiple override layers. Later layers win.
    /// </summary>
    public static RuleSeverityOverrides Merge(params RuleSeverityOverrides[] layers)
    {
        var overrides = new Dictionary<string, FindingSeverity>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var layer in layers)
        {
            foreach (var kv in layer._overrides)
            {
                overrides[kv.Key] = kv.Value;
                sources[kv.Key] = layer.Sources.TryGetValue(kv.Key, out var src)
                    ? src
                    : "(unknown)";
            }
        }

        return new RuleSeverityOverrides(overrides, sources);
    }

    /// <summary>
    /// Default rules file path under the user's local app data folder.
    /// </summary>
    public static string DefaultUserPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MSIXplainer",
        "rules.json");

    private static bool IsKnownRuleId(string ruleId, IReadOnlyCollection<string> known)
    {
        foreach (var k in known)
        {
            if (string.Equals(k, ruleId, StringComparison.OrdinalIgnoreCase)) return true;

            // Dynamic IDs (capability.<name>, device.<name>, network.<name>) match any
            // entry under their prefix. Accept any prefix that has a wildcard entry
            // in the catalog (e.g. "capability.*").
            if (k.EndsWith(".*", StringComparison.Ordinal))
            {
                var prefix = k[..^1]; // keep trailing dot
                if (ruleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
