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
        sb.AppendLine($"| Update impact (block delta — matches SDK) | {diff.TotalDeltaDownloadBytes:N0} | {Human(diff.TotalDeltaDownloadBytes)} |");
        sb.AppendLine($"| + Metadata overhead (blockmap, signature, content-types) | {diff.TotalOverheadBytes:N0} | {Human(diff.TotalOverheadBytes)} |");
        sb.AppendLine($"| = Total wire bytes per device | {diff.TotalUpdateDownloadBytes:N0} | **{Human(diff.TotalUpdateDownloadBytes)}** |");
        sb.AppendLine($"| Savings vs. full install | — | **{savings:F1}%** |");
        sb.AppendLine();

        // Footprint / inventory churn
        var totalAdded = diff.PackageDiffs.Sum(p => p.AddedFilesUncompressedBytes);
        var totalRemoved = diff.PackageDiffs.Sum(p => p.RemovedFilesUncompressedBytes);
        var totalChangedNet = diff.PackageDiffs.Sum(p => p.ChangedFilesNetSizeBytes);
        var totalUnchanged = diff.PackageDiffs.Sum(p => p.UnchangedFilesUncompressedBytes);
        var totalNewSize = diff.PackageDiffs.Sum(p => p.NewPackageUncompressedBytes);
        var addedFiles = diff.PackageDiffs.Sum(p => p.AddedFileCount);
        var removedFiles = diff.PackageDiffs.Sum(p => p.RemovedFileCount);
        var modifiedFiles = diff.PackageDiffs.Sum(p => p.ModifiedFileCount);
        var unchangedFiles = diff.PackageDiffs.Sum(p => p.UnchangedFileCount);

        sb.AppendLine("## File inventory & disk footprint");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count | Uncompressed bytes | Human |");
        sb.AppendLine("|---|---:|---:|---|");
        sb.AppendLine($"| Added files | {addedFiles:N0} | {totalAdded:N0} | {Human(totalAdded)} |");
        sb.AppendLine($"| Deleted files | {removedFiles:N0} | {totalRemoved:N0} | {Human(totalRemoved)} |");
        sb.AppendLine($"| Modified files (net size shift) | {modifiedFiles:N0} | {totalChangedNet:N0} | {SignedHuman(totalChangedNet)} |");
        sb.AppendLine($"| Unchanged files | {unchangedFiles:N0} | {totalUnchanged:N0} | {Human(totalUnchanged)} |");
        sb.AppendLine($"| **New package total (installed)** | — | {totalNewSize:N0} | **{Human(totalNewSize)}** |");
        sb.AppendLine($"| **Additional disk space needed** | — | {diff.TotalInstalledSizeDifferenceBytes:N0} | **{SignedHuman(diff.TotalInstalledSizeDifferenceBytes)}** |");
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
        sb.AppendLine("| Package | Old → New | Full | Delta | Overhead | Total wire | Savings | Blocks reused |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var p in diff.PackageDiffs)
        {
            sb.AppendLine(
                $"| {Esc(p.Label)} | {Esc(p.OldVersion)} → {Esc(p.NewVersion)} | " +
                $"{Human(p.FullDownloadBytes)} | {Human(p.DeltaDownloadBytes)} | " +
                $"{Human(p.OverheadBytes)} | {Human(p.TotalUpdateDownloadBytes)} | " +
                $"{p.SavingsPercent:F1}% | {p.ReusedBlocks}/{p.TotalBlocks} |");
        }
        sb.AppendLine();

        // Duplicate-file optimization opportunities (across all package diffs)
        var allDuplicates = diff.PackageDiffs
            .SelectMany(p => p.DuplicateGroups.Select(g => (Package: p.Label, Group: g)))
            .ToList();

        if (allDuplicates.Count > 0)
        {
            var totalReclaim = allDuplicates.Sum(d => d.Group.PossibleSizeReductionBytes);
            var totalImpactReclaim = allDuplicates.Sum(d => d.Group.PossibleImpactReductionBytes);
            sb.AppendLine($"## Optimization opportunity: {allDuplicates.Count} duplicate file groups");
            sb.AppendLine();
            sb.AppendLine($"The new package contains files with identical content stored under different names. Deduplicating these would reclaim **{Human(totalReclaim)}** of installed footprint and **{Human(totalImpactReclaim)}** of fresh-install download.");
            sb.AppendLine();
            sb.AppendLine("| Package | Copies | Per copy | Reclaimable | Example path |");
            sb.AppendLine("|---|---:|---:|---:|---|");
            foreach (var (pkg, g) in allDuplicates.OrderByDescending(x => x.Group.PossibleSizeReductionBytes).Take(20))
            {
                sb.AppendLine(
                    $"| {Esc(pkg)} | {g.CopyCount} | {Human(g.PerCopyUncompressedBytes)} | " +
                    $"{Human(g.PossibleSizeReductionBytes)} | `{Esc(g.Paths[0])}` |");
            }
            sb.AppendLine();
        }

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
                OverheadBytes = diff.TotalOverheadBytes,
                TotalUpdateDownloadBytes = diff.TotalUpdateDownloadBytes,
                InstalledSizeDifferenceBytes = diff.TotalInstalledSizeDifferenceBytes,
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
                p.TotalUpdateDownloadBytes,
                p.SavingsPercent,
                p.TotalBlocks,
                p.ReusedBlocks,
                p.NewBlocks,
                p.AddedFileCount,
                p.RemovedFileCount,
                p.ModifiedFileCount,
                p.UnchangedFileCount,
                p.NewPackageUncompressedBytes,
                p.OldPackageUncompressedBytes,
                p.AddedFilesUncompressedBytes,
                p.RemovedFilesUncompressedBytes,
                p.ChangedFilesNetSizeBytes,
                p.UnchangedFilesUncompressedBytes,
                p.InstalledSizeDifferenceBytes,
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
                }),
                Duplicates = p.DuplicateGroups.Select(g => new
                {
                    g.Paths,
                    g.CopyCount,
                    g.PerCopyUncompressedBytes,
                    g.PerCopyOnWireBytes,
                    g.PossibleSizeReductionBytes,
                    g.PossibleImpactReductionBytes
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
        if (bytes < 0) return "-" + Human(-bytes);
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        int i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
        return $"{v:F2} {units[i]}";
    }

    /// <summary>Like Human but always shows the sign for non-zero values (useful for deltas).</summary>
    public static string SignedHuman(long bytes)
    {
        if (bytes == 0) return "0 B";
        return (bytes > 0 ? "+" : "") + Human(bytes);
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
