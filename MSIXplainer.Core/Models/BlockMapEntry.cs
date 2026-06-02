namespace MSIXplainer.Models;

/// <summary>
/// One file from an AppxBlockMap.xml.
/// </summary>
public sealed class BlockMapFile
{
    public required string Name { get; init; }
    public required long UncompressedSize { get; init; }
    public required long LfhSize { get; init; }
    public required IReadOnlyList<BlockMapBlock> Blocks { get; init; }

    /// <summary>Sum of on-the-wire block sizes (compressed if compressed, else 64 KB).</summary>
    public long OnWireSize => Blocks.Sum(b => b.OnWireSize);
}

/// <summary>
/// One 64 KB (or smaller, final) block within a file.
/// </summary>
public sealed class BlockMapBlock
{
    /// <summary>Base64 SHA-256 of the block contents.</summary>
    public required string Hash { get; init; }

    /// <summary>
    /// Compressed size if the file is stored compressed; null means the block is
    /// stored uncompressed (so it is the full 64 KB except for the final block of a file).
    /// </summary>
    public required long? CompressedSize { get; init; }

    /// <summary>
    /// Position of this block within the file in 64 KB units (0-based). Used to
    /// compute the uncompressed size of the final block when needed.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>Uncompressed size of this block (always 64 KB except the final block).</summary>
    public required long UncompressedSize { get; init; }

    /// <summary>Bytes that would actually traverse the network for this block.</summary>
    public long OnWireSize => CompressedSize ?? UncompressedSize;
}
