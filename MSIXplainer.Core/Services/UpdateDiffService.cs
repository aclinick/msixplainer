using System.IO.Compression;
using MSIXplainer.Models;

namespace MSIXplainer.Services;

/// <summary>
/// Computes the on-the-wire delta between two MSIX packages by diffing their
/// AppxBlockMap.xml block hashes. This models how App Installer / the Store /
/// MDM-driven updates actually pull bytes: only blocks (≈ 64 KB chunks) whose
/// SHA-256 hash is not already present locally are downloaded.
///
/// Reuse model: per-file path. AppxBlockMap.xml is a per-file structure and the
/// servicing stack matches blocks against the same file in the previously installed
/// version. This is the conservative, realistic estimate — package-wide hash
/// dedup would understate the delta for moved or duplicated content.
/// </summary>
public static class UpdateDiffService
{
    /// <summary>
    /// Fixed metadata entries downloaded on every update because they are NOT
    /// listed as File entries inside AppxBlockMap.xml. AppxManifest.xml is
    /// intentionally excluded — it is itself a File entry inside the block map,
    /// so its bytes already flow through the block-diff path.
    /// </summary>
    private static readonly string[] FixedOverheadEntries =
    [
        "AppxBlockMap.xml",
        "AppxSignature.p7x",
        "[Content_Types].xml"
    ];

    /// <summary>
    /// Compares two single MSIX/APPX packages (not bundles) and returns the diff.
    /// Throws if a path points at a bundle or a file missing the block map.
    /// </summary>
    public static UpdateDiffResult ComparePackages(string oldPackagePath, string newPackagePath)
    {
        if (ManifestParserService.IsBundleFile(oldPackagePath) ||
            ManifestParserService.IsBundleFile(newPackagePath))
        {
            throw new InvalidOperationException(
                "ComparePackages requires single .msix/.appx files. For bundles, use CompareBundles.");
        }

        var oldInfo = ManifestParserService.ExtractFromPackage(oldPackagePath).Info;
        var newInfo = ManifestParserService.ExtractFromPackage(newPackagePath).Info;

        var oldBlockMap = BlockMapParser.ExtractFromPackage(oldPackagePath);
        var newBlockMap = BlockMapParser.ExtractFromPackage(newPackagePath);

        var newOverhead = MeasureOverheadBytes(newPackagePath);
        var newFullSize = new FileInfo(newPackagePath).Length;

        var packageDiff = DiffBlockMaps(
            label: $"{newInfo.DisplayName} ({newInfo.Architecture})",
            oldVersion: oldInfo.Version,
            newVersion: newInfo.Version,
            architecture: newInfo.Architecture,
            oldFiles: oldBlockMap,
            newFiles: newBlockMap,
            overheadBytes: newOverhead,
            fullDownloadBytesOverride: newFullSize);

        var warnings = BuildPackageLevelWarnings(oldInfo, newInfo);

        return new UpdateDiffResult
        {
            OldLabel = $"{oldInfo.DisplayName} {oldInfo.Version}",
            NewLabel = $"{newInfo.DisplayName} {newInfo.Version}",
            PackageDiffs = [packageDiff],
            Warnings = warnings
        };
    }

    /// <summary>
    /// Compares two block-map lists directly. Public for testing and for bundle
    /// orchestration where the caller already has block maps in hand.
    /// </summary>
    /// <param name="overheadBytes">Fixed metadata always re-downloaded for the update (blockmap + signature + content-types).</param>
    /// <param name="fullDownloadBytesOverride">If provided (typical for real .msix paths), used as the fresh-install download baseline instead of the reconstructed block payload + overhead.</param>
    public static PackageDiff DiffBlockMaps(
        string label,
        string oldVersion,
        string newVersion,
        string architecture,
        IReadOnlyList<BlockMapFile> oldFiles,
        IReadOnlyList<BlockMapFile> newFiles,
        long overheadBytes = 0,
        long? fullDownloadBytesOverride = null)
    {
        var oldByPath = oldFiles.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var newByPath = newFiles.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        var fileDiffs = new List<FileDiff>();
        long fullPayloadBytes = 0;
        long deltaPayloadBytes = 0;
        int totalBlocks = 0;
        int reusedBlocks = 0;

        foreach (var newFile in newFiles)
        {
            oldByPath.TryGetValue(newFile.Name, out var oldFile);

            // Per-file reuse: a block hash is only "reused" if the OLD copy of
            // the same-path file contained that same hash.
            var oldHashesForThisFile = oldFile is null
                ? null
                : new HashSet<string>(oldFile.Blocks.Select(b => b.Hash), StringComparer.Ordinal);

            long fileFull = newFile.OnWireSize;
            long fileDelta = 0;
            int fileReused = 0;

            foreach (var block in newFile.Blocks)
            {
                if (oldHashesForThisFile is not null && oldHashesForThisFile.Contains(block.Hash))
                {
                    fileReused++;
                }
                else
                {
                    fileDelta += block.OnWireSize;
                }
            }

            var status = ClassifyFile(oldFile, newFile);

            fileDiffs.Add(new FileDiff
            {
                Path = newFile.Name,
                Status = status,
                NewSize = newFile.UncompressedSize,
                OldSize = oldFile?.UncompressedSize ?? 0,
                DeltaBytes = fileDelta,
                FullBytes = fileFull,
                TotalBlocks = newFile.Blocks.Count,
                ReusedBlocks = fileReused
            });

            fullPayloadBytes += fileFull;
            deltaPayloadBytes += fileDelta;
            totalBlocks += newFile.Blocks.Count;
            reusedBlocks += fileReused;
        }

        // Files removed in new: nothing to download for them, but surface in the UI.
        foreach (var oldFile in oldFiles)
        {
            if (!newByPath.ContainsKey(oldFile.Name))
            {
                fileDiffs.Add(new FileDiff
                {
                    Path = oldFile.Name,
                    Status = FileDiffStatus.Removed,
                    NewSize = 0,
                    OldSize = oldFile.UncompressedSize,
                    DeltaBytes = 0,
                    FullBytes = 0,
                    TotalBlocks = 0,
                    ReusedBlocks = 0
                });
            }
        }

        long delta = deltaPayloadBytes + overheadBytes;
        long full = fullDownloadBytesOverride ?? (fullPayloadBytes + overheadBytes);

        // Delta should never exceed full — synthetic fixtures or fully-changed
        // packages can otherwise tie.
        if (delta > full) delta = full;

        return new PackageDiff
        {
            Label = label,
            OldVersion = oldVersion,
            NewVersion = newVersion,
            Architecture = architecture,
            Files = fileDiffs
                .OrderByDescending(f => f.DeltaBytes)
                .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FullDownloadBytes = full,
            DeltaDownloadBytes = delta,
            OverheadBytes = overheadBytes,
            TotalBlocks = totalBlocks,
            ReusedBlocks = reusedBlocks
        };
    }

    /// <summary>
    /// Unchanged ⇒ same path exists in old, same uncompressed size, same block
    /// count, and same block hash at every index. Anything else is Modified
    /// (when old exists) or Added (when it doesn't).
    /// </summary>
    private static FileDiffStatus ClassifyFile(BlockMapFile? oldFile, BlockMapFile newFile)
    {
        if (oldFile is null) return FileDiffStatus.Added;

        if (oldFile.UncompressedSize != newFile.UncompressedSize) return FileDiffStatus.Modified;
        if (oldFile.Blocks.Count != newFile.Blocks.Count) return FileDiffStatus.Modified;

        for (var i = 0; i < newFile.Blocks.Count; i++)
        {
            if (!string.Equals(oldFile.Blocks[i].Hash, newFile.Blocks[i].Hash, StringComparison.Ordinal))
                return FileDiffStatus.Modified;
        }

        return FileDiffStatus.Unchanged;
    }

    /// <summary>
    /// On-disk (compressed) size of the package's fixed metadata files that
    /// are NOT covered by the block map and are downloaded in full on every update.
    /// </summary>
    internal static long MeasureOverheadBytes(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        long total = 0;
        foreach (var name in FixedOverheadEntries)
        {
            var entry = archive.GetEntry(name);
            if (entry is not null) total += entry.CompressedLength;
        }
        return total;
    }

    private static IReadOnlyList<string> BuildPackageLevelWarnings(PackageInfo oldInfo, PackageInfo newInfo)
    {
        var warnings = new List<string>();

        if (!string.Equals(oldInfo.Name, newInfo.Name, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Package identity differs: old is '{oldInfo.Name}', new is '{newInfo.Name}'. Windows treats these as unrelated packages, so an end-user device would not perform a delta update between them.");

        if (!string.Equals(oldInfo.Publisher, newInfo.Publisher, StringComparison.OrdinalIgnoreCase))
            warnings.Add("Publisher differs between the two packages — Windows requires matching publisher for an in-place update.");

        if (!string.Equals(oldInfo.Architecture, newInfo.Architecture, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Architecture changed ({oldInfo.Architecture} → {newInfo.Architecture}). Devices on the old architecture cannot install the new package.");

        if (Version.TryParse(oldInfo.Version, out var ov) &&
            Version.TryParse(newInfo.Version, out var nv) &&
            nv <= ov)
        {
            warnings.Add($"New version ({newInfo.Version}) is not greater than old version ({oldInfo.Version}). Windows will refuse to apply this as an update.");
        }

        return warnings;
    }
}
