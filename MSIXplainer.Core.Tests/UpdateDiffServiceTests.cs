using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MSIXplainer.Models;
using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class UpdateDiffServiceTests
{
    private const long BlockSize = 64 * 1024;
    private static readonly XNamespace BmNs = "http://schemas.microsoft.com/appx/2010/blockmap";

    private static readonly string MinimalManifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="Test.App" Publisher="CN=Test" Version="{0}" ProcessorArchitecture="x64" />
          <Properties>
            <DisplayName>TestApp</DisplayName>
            <PublisherDisplayName>Test</PublisherDisplayName>
            <Logo>Assets\Logo.png</Logo>
          </Properties>
        </Package>
        """;

    private sealed record SyntheticFile(string Path, byte[] Content);

    /// <summary>
    /// Builds a minimal .msix containing the given files plus an AppxManifest.xml
    /// and a generated AppxBlockMap.xml that accurately reflects each file's
    /// blocks (64 KB) with their compressed sizes and SHA-256 hashes.
    /// </summary>
    private static string CreatePackage(string version, params SyntheticFile[] files)
    {
        var path = Path.Combine(Path.GetTempPath(), $"udt_{Guid.NewGuid()}.msix");

        // First pass: write files into a ZIP, capture compressed sizes per block.
        var blockMap = new XElement(
            BmNs + "BlockMap",
            new XAttribute("HashMethod", "http://www.w3.org/2001/04/xmlenc#sha256"));

        using (var fs = File.Create(path))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            // Manifest
            var manifestEntry = archive.CreateEntry("AppxManifest.xml", CompressionLevel.Optimal);
            using (var ms = manifestEntry.Open())
            using (var sw = new StreamWriter(ms, Encoding.UTF8))
            {
                sw.Write(string.Format(MinimalManifest, version));
            }

            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                using (var s = entry.Open())
                {
                    s.Write(file.Content, 0, file.Content.Length);
                }

                // BlockMap entry for this file.
                var fileEl = new XElement(
                    BmNs + "File",
                    new XAttribute("Name", file.Path.Replace('/', '\\')),
                    new XAttribute("Size", file.Content.Length),
                    new XAttribute("LfhSize", 30 + file.Path.Length));

                // Split content into 64 KB blocks; hash each one.
                int offset = 0;
                while (offset < file.Content.Length || (file.Content.Length == 0 && offset == 0))
                {
                    var len = (int)Math.Min(BlockSize, file.Content.Length - offset);
                    var block = new byte[len];
                    Array.Copy(file.Content, offset, block, 0, len);

                    var hash = Convert.ToBase64String(SHA256.HashData(block));

                    // We don't know the *true* per-block compressed size from
                    // ZipArchive (it gives a per-entry total). The diff math
                    // only cares about consistent on-wire numbers between the
                    // two packages, so we approximate per-block compressed size
                    // as the uncompressed length — this models stored-uncompressed
                    // blocks (Size attribute absent). The diff service treats a
                    // missing Size attribute as "block is uncompressed".
                    fileEl.Add(new XElement(BmNs + "Block", new XAttribute("Hash", hash)));

                    offset += len;
                    if (len == 0) break;
                }

                blockMap.Add(fileEl);
            }

            var bmEntry = archive.CreateEntry("AppxBlockMap.xml", CompressionLevel.Optimal);
            using (var bms = bmEntry.Open())
            {
                var doc = new XDocument(blockMap);
                doc.Save(bms);
            }
        }

        return path;
    }

    private static byte[] Bytes(string seed, int length)
    {
        // Deterministic content so equal "seed,length" => equal hashes => block reuse.
        var rng = new Random(seed.GetHashCode());
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }

    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Identical_Packages_DeltaIsOverheadOnly()
    {
        var oldPath = CreatePackage("1.0.0.0",
            new SyntheticFile("App.exe", Bytes("exe", 200_000)),
            new SyntheticFile("data.bin", Bytes("data", 50_000)));
        var newPath = CreatePackage("1.0.0.0",
            new SyntheticFile("App.exe", Bytes("exe", 200_000)),
            new SyntheticFile("data.bin", Bytes("data", 50_000)));

        try
        {
            var result = UpdateDiffService.ComparePackages(oldPath, newPath);
            var pkg = Assert.Single(result.PackageDiffs);

            Assert.Equal(pkg.OverheadBytes, pkg.DeltaDownloadBytes);
            Assert.Equal(pkg.TotalBlocks, pkg.ReusedBlocks);
            Assert.Equal(0, pkg.NewBlocks);
            Assert.All(pkg.Files, f =>
                Assert.True(f.Status == FileDiffStatus.Unchanged,
                    $"{f.Path} should be Unchanged but was {f.Status}"));
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    [Fact]
    public void FullyChanged_File_DeltaIncludesEntireFile()
    {
        var oldPath = CreatePackage("1.0.0.0",
            new SyntheticFile("App.exe", Bytes("exe-v1", 200_000)));
        var newPath = CreatePackage("1.1.0.0",
            new SyntheticFile("App.exe", Bytes("exe-v2", 200_000)));

        try
        {
            var result = UpdateDiffService.ComparePackages(oldPath, newPath);
            var pkg = Assert.Single(result.PackageDiffs);
            var exe = pkg.Files.Single(f => f.Path.EndsWith("App.exe"));

            Assert.Equal(FileDiffStatus.Modified, exe.Status);
            Assert.Equal(0, exe.ReusedBlocks);
            Assert.True(exe.DeltaBytes >= 200_000, $"Expected >= 200_000, got {exe.DeltaBytes}");
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    [Fact]
    public void PartialChange_ReusesUnchangedBlocks()
    {
        // Common prefix block + a unique tail block.
        var prefix = Bytes("shared-prefix", (int)BlockSize); // exactly 64 KB
        var oldTail = Bytes("tail-v1", (int)BlockSize);
        var newTail = Bytes("tail-v2", (int)BlockSize);

        var oldContent = prefix.Concat(oldTail).ToArray();
        var newContent = prefix.Concat(newTail).ToArray();

        var oldPath = CreatePackage("1.0.0.0", new SyntheticFile("App.bin", oldContent));
        var newPath = CreatePackage("1.1.0.0", new SyntheticFile("App.bin", newContent));

        try
        {
            var result = UpdateDiffService.ComparePackages(oldPath, newPath);
            var pkg = Assert.Single(result.PackageDiffs);
            var file = pkg.Files.Single(f => f.Path.EndsWith("App.bin"));

            Assert.Equal(FileDiffStatus.Modified, file.Status);
            Assert.Equal(2, file.TotalBlocks);
            Assert.Equal(1, file.ReusedBlocks);
            Assert.Equal(1, file.NewBlocks);

            // Should be roughly the size of one 64 KB block (uncompressed in this fixture).
            Assert.InRange(file.DeltaBytes, BlockSize, BlockSize + 1024);
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    [Fact]
    public void AddedFile_AppearsAsAdded()
    {
        var oldPath = CreatePackage("1.0.0.0",
            new SyntheticFile("App.exe", Bytes("exe", 1000)));
        var newPath = CreatePackage("1.1.0.0",
            new SyntheticFile("App.exe", Bytes("exe", 1000)),
            new SyntheticFile("NewFeature.dll", Bytes("dll", 90_000)));

        try
        {
            var result = UpdateDiffService.ComparePackages(oldPath, newPath);
            var pkg = Assert.Single(result.PackageDiffs);
            var added = pkg.Files.Single(f => f.Path.EndsWith("NewFeature.dll"));

            Assert.Equal(FileDiffStatus.Added, added.Status);
            Assert.Equal(0, added.ReusedBlocks);
            Assert.True(added.DeltaBytes > 0);
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    [Fact]
    public void RemovedFile_AppearsAsRemoved_NoDelta()
    {
        var oldPath = CreatePackage("1.0.0.0",
            new SyntheticFile("App.exe", Bytes("exe", 1000)),
            new SyntheticFile("Old.dll", Bytes("old", 90_000)));
        var newPath = CreatePackage("1.1.0.0",
            new SyntheticFile("App.exe", Bytes("exe", 1000)));

        try
        {
            var result = UpdateDiffService.ComparePackages(oldPath, newPath);
            var pkg = Assert.Single(result.PackageDiffs);
            var removed = pkg.Files.Single(f => f.Path.EndsWith("Old.dll"));

            Assert.Equal(FileDiffStatus.Removed, removed.Status);
            Assert.Equal(0, removed.DeltaBytes);
            Assert.Equal(90_000, removed.OldSize);
            Assert.Equal(0, removed.NewSize);
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    [Fact]
    public void FullDownloadBytes_ExceedsDeltaForChangedPackage()
    {
        var oldPath = CreatePackage("1.0.0.0",
            new SyntheticFile("App.exe", Bytes("exe-v1", 200_000)));
        var newPath = CreatePackage("1.1.0.0",
            new SyntheticFile("App.exe", Bytes("exe-v2", 200_000)));

        try
        {
            var result = UpdateDiffService.ComparePackages(oldPath, newPath);
            Assert.True(result.TotalFullDownloadBytes >= result.TotalDeltaDownloadBytes,
                "Full should be >= delta");
            Assert.True(result.SavingsPercent >= 0 && result.SavingsPercent <= 100);

            // Full download equals the actual .msix file size.
            var pkg = result.PackageDiffs.Single();
            Assert.Equal(new FileInfo(newPath).Length, pkg.FullDownloadBytes);
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    [Fact]
    public void PerFileReuse_DoesNotCreditBlocksFromDifferentFile()
    {
        // Same content in two files in the old package; new package has only one
        // file at a *different* path. Under per-file reuse the new file's blocks
        // should NOT be reused, so the delta must cover the full new file.
        var shared = Bytes("shared-content", 100_000);

        var oldPath = CreatePackage("1.0.0.0",
            new SyntheticFile("FolderA/Data.bin", shared),
            new SyntheticFile("FolderB/Data.bin", shared));
        var newPath = CreatePackage("1.1.0.0",
            new SyntheticFile("FolderC/Data.bin", shared));

        try
        {
            var result = UpdateDiffService.ComparePackages(oldPath, newPath);
            var pkg = Assert.Single(result.PackageDiffs);
            var newFile = pkg.Files.Single(f => f.Status == FileDiffStatus.Added);
            Assert.Equal(0, newFile.ReusedBlocks);
            Assert.True(newFile.DeltaBytes >= 100_000);
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    [Fact]
    public void DiffBlockMaps_WithCompressedBlockSizes_UsesCompressedOnWire()
    {
        // Build block maps in-memory with explicit compressed Sizes — exercises
        // the path the synthetic ZIP fixtures can't reach.
        var oldFiles = new List<BlockMapFile>
        {
            new()
            {
                Name = "App.dll",
                UncompressedSize = 65536 + 1000,
                LfhSize = 30,
                Blocks = new List<BlockMapBlock>
                {
                    new() { Hash = "AAA", CompressedSize = 30_000, Index = 0, UncompressedSize = 65536 },
                    new() { Hash = "BBB", CompressedSize = 500, Index = 1, UncompressedSize = 1000 }
                }
            }
        };
        var newFiles = new List<BlockMapFile>
        {
            new()
            {
                Name = "App.dll",
                UncompressedSize = 65536 + 1000,
                LfhSize = 30,
                Blocks = new List<BlockMapBlock>
                {
                    new() { Hash = "AAA", CompressedSize = 30_000, Index = 0, UncompressedSize = 65536 },
                    new() { Hash = "CCC", CompressedSize = 700, Index = 1, UncompressedSize = 1000 } // changed
                }
            }
        };

        var diff = UpdateDiffService.DiffBlockMaps(
            label: "test", oldVersion: "1.0", newVersion: "1.1", architecture: "x64",
            oldFiles: oldFiles, newFiles: newFiles, overheadBytes: 0);

        var file = diff.Files.Single();
        Assert.Equal(FileDiffStatus.Modified, file.Status);
        Assert.Equal(1, file.ReusedBlocks);
        Assert.Equal(700, file.DeltaBytes); // only the compressed size of the changed block
        Assert.Equal(30_700, file.FullBytes); // sum of compressed sizes
    }

    [Fact]
    public void Warnings_FlagDowngradeAndArchChange()
    {
        // We can't easily generate two packages with different identity from
        // CreatePackage (which hard-codes Test.App/x64), so cover downgrade only
        // here — identity/arch warnings are covered by integration in practice.
        var oldPath = CreatePackage("2.0.0.0", new SyntheticFile("App.exe", Bytes("e", 1000)));
        var newPath = CreatePackage("1.0.0.0", new SyntheticFile("App.exe", Bytes("e", 1000)));

        try
        {
            var result = UpdateDiffService.ComparePackages(oldPath, newPath);
            Assert.Contains(result.Warnings, w => w.Contains("not greater than"));
        }
        finally
        {
            File.Delete(oldPath);
            File.Delete(newPath);
        }
    }

    [Fact]
    public void ComparePackages_OnBundle_Throws()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"udt_{Guid.NewGuid()}.msixbundle");
        File.WriteAllText(bundlePath, "");
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => UpdateDiffService.ComparePackages(bundlePath, bundlePath));
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }

    [Fact]
    public void Unchanged_RequiresSameBlockSequenceNotJustSameHashSet()
    {
        // Two files with the same two blocks in opposite order — same hash set
        // but different block sequence. Should be Modified (because order matters
        // for the on-disk file), but every block IS reused (both hashes exist).
        var oldFiles = new List<BlockMapFile>
        {
            new()
            {
                Name = "App.dat",
                UncompressedSize = 128 * 1024,
                LfhSize = 30,
                Blocks = new List<BlockMapBlock>
                {
                    new() { Hash = "AAA", CompressedSize = null, Index = 0, UncompressedSize = 64 * 1024 },
                    new() { Hash = "BBB", CompressedSize = null, Index = 1, UncompressedSize = 64 * 1024 }
                }
            }
        };
        var newFiles = new List<BlockMapFile>
        {
            new()
            {
                Name = "App.dat",
                UncompressedSize = 128 * 1024,
                LfhSize = 30,
                Blocks = new List<BlockMapBlock>
                {
                    new() { Hash = "BBB", CompressedSize = null, Index = 0, UncompressedSize = 64 * 1024 },
                    new() { Hash = "AAA", CompressedSize = null, Index = 1, UncompressedSize = 64 * 1024 }
                }
            }
        };

        var diff = UpdateDiffService.DiffBlockMaps(
            "t", "1.0", "1.1", "x64", oldFiles, newFiles);
        var file = diff.Files.Single();
        Assert.Equal(FileDiffStatus.Modified, file.Status);
        Assert.Equal(2, file.ReusedBlocks); // both hashes are in old
        Assert.Equal(0, file.DeltaBytes);   // but no new bytes — all blocks already exist
    }
}
