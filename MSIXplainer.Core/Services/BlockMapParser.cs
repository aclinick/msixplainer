using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using MSIXplainer.Models;

namespace MSIXplainer.Services;

/// <summary>
/// Parses AppxBlockMap.xml from an MSIX/APPX package. Applies the same
/// untrusted-input hardening as ManifestParserService (DTD off, no resolver,
/// size cap, no code execution).
/// </summary>
public static class BlockMapParser
{
    private static readonly XNamespace Ns =
        "http://schemas.microsoft.com/appx/2010/blockmap";

    private const long DefaultBlockSize = 64 * 1024;
    private const long MaxBlockMapBytes = 50 * 1024 * 1024;

    public const string BlockMapEntryName = "AppxBlockMap.xml";

    public static IReadOnlyList<BlockMapFile> ExtractFromPackage(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        return ExtractFromArchive(archive);
    }

    public static IReadOnlyList<BlockMapFile> ExtractFromArchive(ZipArchive archive)
    {
        var entry = archive.GetEntry(BlockMapEntryName)
            ?? throw new InvalidOperationException(
                "This package does not contain an AppxBlockMap.xml — update-diff requires a valid signed MSIX/APPX package.");

        if (entry.Length > MaxBlockMapBytes)
            throw new InvalidOperationException(
                $"AppxBlockMap.xml exceeds {MaxBlockMapBytes / (1024 * 1024)} MB — refusing to parse.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd();
        return Parse(xml);
    }

    public static IReadOnlyList<BlockMapFile> Parse(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaxBlockMapBytes
        };

        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        var doc = XDocument.Load(xmlReader);

        var root = doc.Root
            ?? throw new InvalidOperationException("AppxBlockMap.xml has no root element.");

        var files = new List<BlockMapFile>();

        foreach (var fileElem in root.Elements(Ns + "File"))
        {
            var rawName = fileElem.Attribute("Name")?.Value
                ?? throw new InvalidOperationException("AppxBlockMap.xml File element missing Name.");
            var name = NormalizePath(rawName);

            var sizeAttr = fileElem.Attribute("Size")?.Value;
            if (!long.TryParse(sizeAttr, out var fileSize) || fileSize < 0)
                throw new InvalidOperationException($"AppxBlockMap.xml File '{name}' has invalid Size.");

            var lfhAttr = fileElem.Attribute("LfhSize")?.Value;
            long.TryParse(lfhAttr, out var lfhSize);

            var blockElems = fileElem.Elements(Ns + "Block").ToList();
            var blocks = new List<BlockMapBlock>(blockElems.Count);

            for (var i = 0; i < blockElems.Count; i++)
            {
                var be = blockElems[i];
                var hash = be.Attribute("Hash")?.Value
                    ?? throw new InvalidOperationException($"AppxBlockMap.xml block in '{name}' missing Hash.");

                long? compressedSize = null;
                var compAttr = be.Attribute("Size")?.Value;
                if (compAttr is not null)
                {
                    if (!long.TryParse(compAttr, out var cs) || cs <= 0)
                        throw new InvalidOperationException(
                            $"AppxBlockMap.xml block {i} in '{name}' has invalid Size — must be a positive integer.");
                    compressedSize = cs;
                }

                // Uncompressed size: 64 KB for every block except possibly the last.
                long uncompressed = DefaultBlockSize;
                if (i == blockElems.Count - 1)
                {
                    var remainder = fileSize - (long)i * DefaultBlockSize;
                    uncompressed = remainder > 0 ? remainder : (fileSize == 0 ? 0 : DefaultBlockSize);
                }

                blocks.Add(new BlockMapBlock
                {
                    Hash = hash,
                    CompressedSize = compressedSize,
                    Index = i,
                    UncompressedSize = uncompressed
                });
            }

            files.Add(new BlockMapFile
            {
                Name = name,
                UncompressedSize = fileSize,
                LfhSize = lfhSize,
                Blocks = blocks
            });
        }

        return files;
    }

    /// <summary>
    /// Normalises a block-map file path: backslashes (the AppxBlockMap convention),
    /// no leading slash. Casing is preserved — callers compare ordinal-ignore-case.
    /// </summary>
    internal static string NormalizePath(string raw)
    {
        var s = raw.Replace('/', '\\').TrimStart('\\');
        // Collapse duplicate separators.
        while (s.Contains(@"\\")) s = s.Replace(@"\\", @"\");
        return s;
    }
}
