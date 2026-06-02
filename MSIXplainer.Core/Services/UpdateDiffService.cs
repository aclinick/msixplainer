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
    /// Compares two .msixbundle / .appxbundle files by pairing inner packages
    /// on architecture (for application packages) and ResourceId (for resource
    /// packages). Each matched pair is diffed via the block-map algorithm and
    /// aggregated into a single result. Unmatched inner packages are surfaced
    /// in AddedPackages / RemovedPackages.
    /// </summary>
    public static UpdateDiffResult CompareBundles(string oldBundlePath, string newBundlePath)
    {
        if (!ManifestParserService.IsBundleFile(oldBundlePath) ||
            !ManifestParserService.IsBundleFile(newBundlePath))
        {
            throw new InvalidOperationException(
                "CompareBundles requires .msixbundle/.appxbundle inputs. For single packages, use ComparePackages.");
        }

        var oldInners = BundleManifestParser.ExtractFromBundle(oldBundlePath);
        var newInners = BundleManifestParser.ExtractFromBundle(newBundlePath);

        var oldByKey = oldInners.ToDictionary(p => p.MatchKey, StringComparer.Ordinal);
        var newByKey = newInners.ToDictionary(p => p.MatchKey, StringComparer.Ordinal);

        var packageDiffs = new List<PackageDiff>();
        var added = new List<string>();
        var removed = new List<string>();

        // Open both bundle ZIPs once for the duration of the diff.
        using var oldArchive = ZipFile.OpenRead(oldBundlePath);
        using var newArchive = ZipFile.OpenRead(newBundlePath);

        foreach (var newInner in newInners)
        {
            if (!oldByKey.TryGetValue(newInner.MatchKey, out var oldInner))
            {
                added.Add(newInner.Label);
                continue;
            }

            var oldBlockMap = ReadInnerBlockMap(oldArchive, oldInner.FileName);
            var newBlockMap = ReadInnerBlockMap(newArchive, newInner.FileName);
            var newOverhead = MeasureInnerOverheadBytes(newArchive, newInner.FileName);

            packageDiffs.Add(DiffBlockMaps(
                label: newInner.Label,
                oldVersion: oldInner.Version,
                newVersion: newInner.Version,
                architecture: newInner.Architecture,
                oldFiles: oldBlockMap,
                newFiles: newBlockMap,
                overheadBytes: newOverhead,
                fullDownloadBytesOverride: newInner.Size > 0 ? newInner.Size : null));
        }

        foreach (var oldInner in oldInners)
        {
            if (!newByKey.ContainsKey(oldInner.MatchKey))
                removed.Add(oldInner.Label);
        }

        var warnings = BuildBundleLevelWarnings(oldInners, newInners);

        return new UpdateDiffResult
        {
            OldLabel = Path.GetFileName(oldBundlePath),
            NewLabel = Path.GetFileName(newBundlePath),
            PackageDiffs = packageDiffs,
            AddedPackages = added,
            RemovedPackages = removed,
            Warnings = warnings
        };
    }

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

        long addedUncompressed = 0;
        long removedUncompressed = 0;
        long changedNetSize = 0;
        long unchangedUncompressed = 0;
        long newPackageUncompressed = 0;

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
            newPackageUncompressed += newFile.UncompressedSize;

            switch (status)
            {
                case FileDiffStatus.Added:
                    addedUncompressed += newFile.UncompressedSize;
                    break;
                case FileDiffStatus.Modified:
                    changedNetSize += newFile.UncompressedSize - (oldFile?.UncompressedSize ?? 0);
                    break;
                case FileDiffStatus.Unchanged:
                    unchangedUncompressed += newFile.UncompressedSize;
                    break;
            }
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
                removedUncompressed += oldFile.UncompressedSize;
            }
        }

        long oldPackageUncompressed = oldFiles.Sum(f => f.UncompressedSize);

        // Delta = pure block-delta payload, matching SDK comparepackage.exe `UpdateImpact`.
        // Metadata overhead is kept SEPARATE; renderers add them for "real wire bytes".
        long delta = deltaPayloadBytes;
        long full = fullDownloadBytesOverride ?? (fullPayloadBytes + overheadBytes);

        // Delta + overhead should never exceed full — synthetic fixtures or fully-changed
        // packages can otherwise tie.
        if (delta + overheadBytes > full) delta = Math.Max(0, full - overheadBytes);

        var duplicates = FindDuplicateGroups(newFiles);

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
            ReusedBlocks = reusedBlocks,
            NewPackageUncompressedBytes = newPackageUncompressed,
            OldPackageUncompressedBytes = oldPackageUncompressed,
            AddedFilesUncompressedBytes = addedUncompressed,
            RemovedFilesUncompressedBytes = removedUncompressed,
            ChangedFilesNetSizeBytes = changedNetSize,
            UnchangedFilesUncompressedBytes = unchangedUncompressed,
            DuplicateGroups = duplicates
        };
    }

    /// <summary>
    /// Groups files within a single package that are byte-identical to each
    /// other (same block-hash sequence). The first hit of each group is the
    /// "original"; subsequent copies could be deduplicated by the package
    /// author. Skips zero-byte files (uninteresting). Mirrors the SDK
    /// comparepackage.exe Duplicate detection.
    /// </summary>
    public static IReadOnlyList<DuplicateFileGroup> FindDuplicateGroups(IReadOnlyList<BlockMapFile> files)
    {
        // Key = concatenated block hashes (or the marker "EMPTY" for zero-byte
        // files we deliberately exclude below).
        var groups = new Dictionary<string, List<BlockMapFile>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (file.Blocks.Count == 0 || file.UncompressedSize == 0)
                continue;

            var key = string.Join('|', file.Blocks.Select(b => b.Hash));
            if (!groups.TryGetValue(key, out var bucket))
            {
                bucket = [];
                groups[key] = bucket;
            }
            bucket.Add(file);
        }

        var result = new List<DuplicateFileGroup>();
        foreach (var (_, bucket) in groups)
        {
            if (bucket.Count < 2) continue;

            var representative = bucket[0];
            result.Add(new DuplicateFileGroup
            {
                Paths = bucket.Select(f => f.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                PerCopyUncompressedBytes = representative.UncompressedSize,
                PerCopyOnWireBytes = representative.OnWireSize
            });
        }

        return result
            .OrderByDescending(g => g.PossibleImpactReductionBytes)
            .ThenByDescending(g => g.CopyCount)
            .ToList();
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
        return MeasureOverheadBytesInArchive(archive);
    }

    /// <summary>
    /// Overhead measurement for an inner .msix entry within an open bundle archive.
    /// </summary>
    internal static long MeasureInnerOverheadBytes(ZipArchive bundleArchive, string innerFileName)
    {
        using var innerArchive = OpenInnerArchive(bundleArchive, innerFileName);
        return MeasureOverheadBytesInArchive(innerArchive);
    }

    private static long MeasureOverheadBytesInArchive(ZipArchive archive)
    {
        long total = 0;
        foreach (var name in FixedOverheadEntries)
        {
            var entry = archive.GetEntry(name);
            if (entry is not null) total += entry.CompressedLength;
        }
        return total;
    }

    private static IReadOnlyList<BlockMapFile> ReadInnerBlockMap(ZipArchive bundleArchive, string innerFileName)
    {
        using var innerArchive = OpenInnerArchive(bundleArchive, innerFileName);
        return BlockMapParser.ExtractFromArchive(innerArchive);
    }

    /// <summary>
    /// Opens an inner .msix entry inside a bundle as its own in-memory ZIP archive.
    /// Caller is responsible for disposing the returned archive (its backing MemoryStream
    /// is owned by the archive).
    /// </summary>
    private static ZipArchive OpenInnerArchive(ZipArchive bundleArchive, string innerFileName)
    {
        var entry = bundleArchive.GetEntry(innerFileName)
            ?? throw new InvalidOperationException(
                $"Bundle does not contain expected inner package '{innerFileName}'.");

        // Guard: skip unreasonably large inner packages (500 MB), matching the
        // limit used by ManifestParserService.ExtractFromBundle.
        if (entry.Length > 500 * 1024 * 1024)
            throw new InvalidOperationException(
                $"Inner package '{innerFileName}' exceeds 500 MB — refusing to process.");

        var memory = new MemoryStream(capacity: (int)Math.Min(entry.Length, int.MaxValue));
        using (var s = entry.Open())
        {
            s.CopyTo(memory);
        }
        memory.Position = 0;
        return new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
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

    private static IReadOnlyList<string> BuildBundleLevelWarnings(
        IReadOnlyList<BundleInnerPackage> oldInners,
        IReadOnlyList<BundleInnerPackage> newInners)
    {
        var warnings = new List<string>();

        var oldArchs = oldInners.Where(p => p.IsApplication).Select(p => p.Architecture).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newArchs = newInners.Where(p => p.IsApplication).Select(p => p.Architecture).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var droppedArchs = oldArchs.Except(newArchs, StringComparer.OrdinalIgnoreCase).ToList();
        if (droppedArchs.Count > 0)
            warnings.Add($"New bundle no longer ships these architectures: {string.Join(", ", droppedArchs)}. Devices on those architectures will not receive an update.");

        var addedArchs = newArchs.Except(oldArchs, StringComparer.OrdinalIgnoreCase).ToList();
        if (addedArchs.Count > 0)
            warnings.Add($"New bundle adds these architectures: {string.Join(", ", addedArchs)}. Devices newly covered will perform a full install rather than a delta update.");

        return warnings;
    }
}
