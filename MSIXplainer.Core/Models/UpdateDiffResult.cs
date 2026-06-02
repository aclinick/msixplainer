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
    public long TotalOverheadBytes => PackageDiffs.Sum(p => p.OverheadBytes);

    /// <summary>
    /// Total bytes a device actually pulls over the network for the update —
    /// block-delta payload plus fixed metadata (AppxBlockMap.xml, AppxSignature.p7x,
    /// [Content_Types].xml). Use this for bandwidth planning. The block-delta
    /// number alone (TotalDeltaDownloadBytes) matches the Windows SDK
    /// comparepackage.exe `UpdateImpact` value to the byte.
    /// </summary>
    public long TotalUpdateDownloadBytes => TotalDeltaDownloadBytes + TotalOverheadBytes;

    /// <summary>Sum of (NewPackageUncompressedBytes − OldPackageUncompressedBytes) over all packages — the net installed-footprint change.</summary>
    public long TotalInstalledSizeDifferenceBytes => PackageDiffs.Sum(p => p.InstalledSizeDifferenceBytes);

    public double SavingsPercent =>
        TotalFullDownloadBytes == 0
            ? 0
            : 100.0 * (TotalFullDownloadBytes - TotalUpdateDownloadBytes) / TotalFullDownloadBytes;
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
    /// On-wire bytes the device downloads for changed payload blocks: the sum of
    /// on-wire block sizes for blocks in the new package whose hashes are not
    /// present in the same-path file of the old package. Matches the Windows
    /// SDK comparepackage.exe `Package/@UpdateImpact` value exactly (which is
    /// itself defined as AddedFiles.UpdateImpact + ChangedFiles.UpdateImpact).
    /// Does NOT include the fixed metadata overhead — see OverheadBytes and
    /// TotalUpdateDownloadBytes for that.
    /// </summary>
    public required long DeltaDownloadBytes { get; init; }

    /// <summary>
    /// Bytes of fixed package metadata always re-downloaded
    /// (AppxBlockMap.xml + AppxSignature.p7x + [Content_Types].xml).
    /// AppxManifest.xml is excluded here because it is itself listed as a
    /// File entry inside the block map and flows through the block diff.
    /// </summary>
    public required long OverheadBytes { get; init; }

    /// <summary>Total wire bytes pulled for this update = DeltaDownloadBytes + OverheadBytes.</summary>
    public long TotalUpdateDownloadBytes => DeltaDownloadBytes + OverheadBytes;

    /// <summary>Total blocks in the new package.</summary>
    public required int TotalBlocks { get; init; }

    /// <summary>Blocks in the new package whose hash already exists in the old package.</summary>
    public required int ReusedBlocks { get; init; }

    public int NewBlocks => TotalBlocks - ReusedBlocks;

    /// <summary>Sum of uncompressed sizes of all files in the new package (matches SDK `Package/@Size`).</summary>
    public required long NewPackageUncompressedBytes { get; init; }

    /// <summary>Sum of uncompressed sizes of all files in the old package.</summary>
    public required long OldPackageUncompressedBytes { get; init; }

    /// <summary>Sum of uncompressed sizes of files added in the new package (matches SDK `Package/@AddedSize`).</summary>
    public required long AddedFilesUncompressedBytes { get; init; }

    /// <summary>Sum of uncompressed sizes of files removed in the new package (matches SDK `Package/@DeletedSize`).</summary>
    public required long RemovedFilesUncompressedBytes { get; init; }

    /// <summary>Net size change across modified files (Σ NewSize − OldSize) — matches SDK `ChangedFiles/@SizeDifference`.</summary>
    public required long ChangedFilesNetSizeBytes { get; init; }

    /// <summary>Sum of uncompressed sizes of files identical in both packages.</summary>
    public required long UnchangedFilesUncompressedBytes { get; init; }

    /// <summary>
    /// Net installed-footprint change after applying the update (matches SDK
    /// `Package/@SizeDifference`). Equivalent to NewPackageUncompressedBytes −
    /// OldPackageUncompressedBytes. Useful for capacity planning: "will the
    /// upgraded package still fit?".
    /// </summary>
    public long InstalledSizeDifferenceBytes =>
        NewPackageUncompressedBytes - OldPackageUncompressedBytes;

    /// <summary>
    /// Groups of files in the new package that are byte-identical to each other
    /// (same block-hash sequence). Each group surfaces the bytes the package
    /// author could save by deduplicating the content.
    /// </summary>
    public IReadOnlyList<DuplicateFileGroup> DuplicateGroups { get; init; } = [];

    public double SavingsPercent =>
        FullDownloadBytes == 0
            ? 0
            : 100.0 * (FullDownloadBytes - TotalUpdateDownloadBytes) / FullDownloadBytes;

    public int AddedFileCount => Files.Count(f => f.Status == FileDiffStatus.Added);
    public int RemovedFileCount => Files.Count(f => f.Status == FileDiffStatus.Removed);
    public int ModifiedFileCount => Files.Count(f => f.Status == FileDiffStatus.Modified);
    public int UnchangedFileCount => Files.Count(f => f.Status == FileDiffStatus.Unchanged);
}

/// <summary>
/// A set of files in the new package that are byte-identical (same block-hash
/// sequence). Mirrors the SDK comparepackage.exe `Duplicate` entries — exposes
/// how much the package author could save by deduplicating the content.
/// </summary>
public sealed class DuplicateFileGroup
{
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>Uncompressed size of one copy of the duplicated content.</summary>
    public required long PerCopyUncompressedBytes { get; init; }

    /// <summary>On-wire bytes for one copy (compressed if compressed in package).</summary>
    public required long PerCopyOnWireBytes { get; init; }

    public int CopyCount => Paths.Count;

    /// <summary>Uncompressed bytes that could be reclaimed if dedup'd.</summary>
    public long PossibleSizeReductionBytes => (CopyCount - 1) * PerCopyUncompressedBytes;

    /// <summary>On-wire bytes that could be saved on a fresh install if dedup'd.</summary>
    public long PossibleImpactReductionBytes => (CopyCount - 1) * PerCopyOnWireBytes;
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
