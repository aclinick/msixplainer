namespace MSIXplainer.Models;

/// <summary>
/// Top-level result of comparing two MSIX packages or bundles.
/// </summary>
public sealed class UpdateDiffResult
{
    public required string OldLabel { get; init; }
    public required string NewLabel { get; init; }

    /// <summary>
    /// One entry per (old, new) package pairing. A single .msix vs .msix comparison
    /// yields one entry; bundle comparisons yield one per matched architecture/qualifier.
    /// </summary>
    public required IReadOnlyList<PackageDiff> PackageDiffs { get; init; }

    /// <summary>Inner packages present only in the new bundle (no old counterpart).</summary>
    public IReadOnlyList<string> AddedPackages { get; init; } = [];

    /// <summary>Inner packages present only in the old bundle (no new counterpart).</summary>
    public IReadOnlyList<string> RemovedPackages { get; init; } = [];

    /// <summary>
    /// Caveats surfaced for the comparison as a whole: identity/architecture mismatch,
    /// downgrade, missing block map, etc. UI/CLI should display these prominently.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public long TotalFullDownloadBytes => PackageDiffs.Sum(p => p.FullDownloadBytes);
    public long TotalDeltaDownloadBytes => PackageDiffs.Sum(p => p.DeltaDownloadBytes);

    public double SavingsPercent =>
        TotalFullDownloadBytes == 0
            ? 0
            : 100.0 * (TotalFullDownloadBytes - TotalDeltaDownloadBytes) / TotalFullDownloadBytes;
}

/// <summary>
/// Diff between one pair of inner packages (or a single package-to-package comparison).
/// </summary>
public sealed class PackageDiff
{
    public required string Label { get; init; }
    public required string OldVersion { get; init; }
    public required string NewVersion { get; init; }
    public required string Architecture { get; init; }

    public required IReadOnlyList<FileDiff> Files { get; init; }

    /// <summary>
    /// Bytes a device downloads for a fresh install of the new package. For a
    /// real .msix this is the file size (ZIP container + payload + signature).
    /// For synthetic or bundle-aggregated cases it falls back to the block
    /// payload sum plus overhead.
    /// </summary>
    public required long FullDownloadBytes { get; init; }

    /// <summary>
    /// On-wire bytes that must be downloaded for the update: the sum of on-wire
    /// block sizes for blocks in the new package whose hashes are not present
    /// in the same-path file of the old package, plus the fixed overhead.
    /// </summary>
    public required long DeltaDownloadBytes { get; init; }

    /// <summary>
    /// Bytes of fixed package metadata always re-downloaded
    /// (AppxBlockMap.xml + AppxSignature.p7x + [Content_Types].xml).
    /// AppxManifest.xml is excluded here because it is itself listed as a
    /// File entry inside the block map and flows through the block diff.
    /// </summary>
    public required long OverheadBytes { get; init; }

    /// <summary>Total blocks in the new package.</summary>
    public required int TotalBlocks { get; init; }

    /// <summary>Blocks in the new package whose hash already exists in the old package.</summary>
    public required int ReusedBlocks { get; init; }

    public int NewBlocks => TotalBlocks - ReusedBlocks;

    public double SavingsPercent =>
        FullDownloadBytes == 0
            ? 0
            : 100.0 * (FullDownloadBytes - DeltaDownloadBytes) / FullDownloadBytes;

    public int AddedFileCount => Files.Count(f => f.Status == FileDiffStatus.Added);
    public int RemovedFileCount => Files.Count(f => f.Status == FileDiffStatus.Removed);
    public int ModifiedFileCount => Files.Count(f => f.Status == FileDiffStatus.Modified);
    public int UnchangedFileCount => Files.Count(f => f.Status == FileDiffStatus.Unchanged);
}

/// <summary>
/// Per-file diff entry showing how much of one file is new on the wire.
/// </summary>
public sealed class FileDiff
{
    public required string Path { get; init; }
    public required FileDiffStatus Status { get; init; }

    /// <summary>Uncompressed file size in the new package (0 for Removed).</summary>
    public required long NewSize { get; init; }

    /// <summary>Uncompressed file size in the old package (0 for Added).</summary>
    public required long OldSize { get; init; }

    /// <summary>On-wire bytes downloaded for this file's blocks under the update.</summary>
    public required long DeltaBytes { get; init; }

    /// <summary>On-wire bytes downloaded for this file in a fresh install of the new package.</summary>
    public required long FullBytes { get; init; }

    /// <summary>Blocks belonging to this file in the new package.</summary>
    public required int TotalBlocks { get; init; }

    /// <summary>Blocks reused from the old package via hash match.</summary>
    public required int ReusedBlocks { get; init; }

    public int NewBlocks => TotalBlocks - ReusedBlocks;

    public double ReusePercent =>
        TotalBlocks == 0 ? 0 : 100.0 * ReusedBlocks / TotalBlocks;
}

public enum FileDiffStatus
{
    Unchanged,
    Modified,
    Added,
    Removed
}
