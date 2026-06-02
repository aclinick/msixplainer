using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSIXplainer.Models;

namespace MSIXplainer.Services;

/// <summary>
/// Renders UpdateDiffResult (with optional bandwidth projection) to Markdown
/// or JSON for IT-pro reporting.
/// </summary>
public static class DiffExportService
{
    public static string ExportToMarkdown(
        UpdateDiffResult diff,
        BandwidthEstimate? bandwidth = null,
        int topFiles = 25)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# MSIX Update Impact: {diff.OldLabel} → {diff.NewLabel}");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        // Headline
        var savings = diff.SavingsPercent;
        sb.AppendLine("## Headline");
        sb.AppendLine();
        sb.AppendLine("| | Bytes | Human |");
        sb.AppendLine("|---|---:|---|");
        sb.AppendLine($"| Fresh install (full download) | {diff.TotalFullDownloadBytes:N0} | {Human(diff.TotalFullDownloadBytes)} |");
        sb.AppendLine($"| Update (delta download per device) | {diff.TotalDeltaDownloadBytes:N0} | {Human(diff.TotalDeltaDownloadBytes)} |");
        sb.AppendLine($"| Savings vs. full install | — | **{savings:F1}%** |");
        sb.AppendLine();

        // Warnings
        if (diff.Warnings.Count > 0)
        {
            sb.AppendLine("## ⚠ Warnings");
            sb.AppendLine();
            foreach (var w in diff.Warnings)
                sb.AppendLine($"- {Esc(w)}");
            sb.AppendLine();
        }

        // Added / removed inner packages
        if (diff.AddedPackages.Count > 0 || diff.RemovedPackages.Count > 0)
        {
            sb.AppendLine("## Bundle composition changes");
            sb.AppendLine();
            if (diff.AddedPackages.Count > 0)
                sb.AppendLine($"- **Added inner packages:** {string.Join(", ", diff.AddedPackages.Select(Esc))}");
            if (diff.RemovedPackages.Count > 0)
                sb.AppendLine($"- **Removed inner packages:** {string.Join(", ", diff.RemovedPackages.Select(Esc))}");
            sb.AppendLine();
        }

        // Per-package breakdown
        sb.AppendLine("## Per-package breakdown");
        sb.AppendLine();
        sb.AppendLine("| Package | Old → New | Full | Delta | Savings | Blocks reused |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var p in diff.PackageDiffs)
        {
            sb.AppendLine(
                $"| {Esc(p.Label)} | {Esc(p.OldVersion)} → {Esc(p.NewVersion)} | " +
                $"{Human(p.FullDownloadBytes)} | {Human(p.DeltaDownloadBytes)} | " +
                $"{p.SavingsPercent:F1}% | {p.ReusedBlocks}/{p.TotalBlocks} |");
        }
        sb.AppendLine();

        // Top changed files (collected across all package diffs)
        var topFileChanges = diff.PackageDiffs
            .SelectMany(p => p.Files.Select(f => (Package: p.Label, File: f)))
            .Where(x => x.File.DeltaBytes > 0 || x.File.Status == FileDiffStatus.Removed)
            .OrderByDescending(x => x.File.DeltaBytes)
            .Take(topFiles)
            .ToList();

        if (topFileChanges.Count > 0)
        {
            sb.AppendLine($"## Top {topFileChanges.Count} files by delta");
            sb.AppendLine();
            sb.AppendLine("| Package | File | Status | Delta | New size | Old size | Blocks reused |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---:|");
            foreach (var (pkg, file) in topFileChanges)
            {
                sb.AppendLine(
                    $"| {Esc(pkg)} | `{Esc(file.Path)}` | {file.Status} | " +
                    $"{Human(file.DeltaBytes)} | {Human(file.NewSize)} | {Human(file.OldSize)} | " +
                    $"{file.ReusedBlocks}/{file.TotalBlocks} |");
            }
            sb.AppendLine();
        }

        // Bandwidth projection
        if (bandwidth is not null)
        {
            sb.AppendLine("## Bandwidth & cost projection");
            sb.AppendLine();
            sb.AppendLine($"- **Delta per device:** {Human(bandwidth.DeltaBytesPerDevice)} ({bandwidth.DeltaBytesPerDevice:N0} bytes)");
            sb.AppendLine($"- **Device count:** {bandwidth.DeviceCount:N0}");
            sb.AppendLine($"- **Total transfer:** {Human(bandwidth.TotalBytes)} ({bandwidth.TotalBytes:N0} bytes)");
            if (bandwidth.EstimatedCostUsd is { } cost)
                sb.AppendLine($"- **Estimated egress cost:** ${cost:N2} USD (at ${bandwidth.CostPerGigabyteUsd:N3}/GB)");
            sb.AppendLine();

            sb.AppendLine("| Link speed | Per device | Serial fleet |");
            sb.AppendLine("|---:|---|---|");
            foreach (var p in bandwidth.LinkProjections)
            {
                sb.AppendLine($"| {p.LinkSpeedMbps:N0} Mbps | {FormatDuration(p.PerDeviceDuration)} | {FormatDuration(p.SerialFleetDuration)} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*Delta size is estimated from AppxBlockMap.xml block hashes, mirroring how App Installer / Microsoft Store / MDM-driven updates download only changed 64 KB blocks. Actual on-the-wire bytes can vary slightly due to TLS framing, HTTP/2 overhead, and CDN behavior.*");

        return sb.ToString();
    }

    public static string ExportToJson(UpdateDiffResult diff, BandwidthEstimate? bandwidth = null)
    {
        var report = new
        {
            diff.OldLabel,
            diff.NewLabel,
            Totals = new
            {
                FullDownloadBytes = diff.TotalFullDownloadBytes,
                DeltaDownloadBytes = diff.TotalDeltaDownloadBytes,
                diff.SavingsPercent
            },
            diff.Warnings,
            diff.AddedPackages,
            diff.RemovedPackages,
            Packages = diff.PackageDiffs.Select(p => new
            {
                p.Label,
                p.Architecture,
                p.OldVersion,
                p.NewVersion,
                p.FullDownloadBytes,
                p.DeltaDownloadBytes,
                p.OverheadBytes,
                p.SavingsPercent,
                p.TotalBlocks,
                p.ReusedBlocks,
                p.NewBlocks,
                p.AddedFileCount,
                p.RemovedFileCount,
                p.ModifiedFileCount,
                p.UnchangedFileCount,
                Files = p.Files.Select(f => new
                {
                    f.Path,
                    Status = f.Status.ToString(),
                    f.NewSize,
                    f.OldSize,
                    f.FullBytes,
                    f.DeltaBytes,
                    f.TotalBlocks,
                    f.ReusedBlocks,
                    f.NewBlocks
                })
            }),
            Bandwidth = bandwidth is null ? null : new
            {
                bandwidth.DeltaBytesPerDevice,
                bandwidth.DeviceCount,
                bandwidth.TotalBytes,
                bandwidth.CostPerGigabyteUsd,
                bandwidth.EstimatedCostUsd,
                LinkProjections = bandwidth.LinkProjections.Select(l => new
                {
                    l.LinkSpeedMbps,
                    PerDeviceSeconds = l.PerDeviceDuration.TotalSeconds,
                    SerialFleetSeconds = l.SerialFleetDuration.TotalSeconds
                })
            },
            GeneratedAt = DateTime.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    public static string Human(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        int i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
        return $"{v:F2} {units[i]}";
    }

    public static string FormatDuration(TimeSpan d)
    {
        if (d.TotalSeconds < 1) return $"{d.TotalMilliseconds:F0} ms";
        if (d.TotalSeconds < 60) return $"{d.TotalSeconds:F1} s";
        if (d.TotalMinutes < 60) return $"{d.TotalMinutes:F1} min";
        if (d.TotalHours < 24) return $"{d.TotalHours:F1} h";
        return $"{d.TotalDays:F1} d";
    }

    private static string Esc(string text) =>
        text.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}
