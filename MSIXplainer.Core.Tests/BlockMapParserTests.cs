using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class BlockMapParserTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap"
                  HashMethod="http://www.w3.org/2001/04/xmlenc#sha256">
          <File Name="App.exe" Size="100000" LfhSize="38">
            <Block Hash="AAAA" Size="40000" />
            <Block Hash="BBBB" Size="20000" />
          </File>
          <File Name="Empty.txt" Size="0" LfhSize="30">
          </File>
          <File Name="Uncompressed.bin" Size="131072" LfhSize="44">
            <Block Hash="CCCC" />
            <Block Hash="DDDD" />
          </File>
        </BlockMap>
        """;

    [Fact]
    public void Parses_File_Block_AndSizes()
    {
        var files = BlockMapParser.Parse(SampleXml);

        Assert.Equal(3, files.Count);

        var exe = files[0];
        Assert.Equal("App.exe", exe.Name);
        Assert.Equal(100_000, exe.UncompressedSize);
        Assert.Equal(2, exe.Blocks.Count);
        Assert.Equal(40_000, exe.Blocks[0].CompressedSize);
        Assert.Equal(64 * 1024, exe.Blocks[0].UncompressedSize); // not the last block
        Assert.Equal(100_000 - 64 * 1024, exe.Blocks[1].UncompressedSize); // remainder
        Assert.Equal(60_000, exe.OnWireSize); // 40_000 + 20_000

        var empty = files[1];
        Assert.Equal(0, empty.UncompressedSize);
        Assert.Empty(empty.Blocks);

        var uncompressed = files[2];
        Assert.Equal(131_072, uncompressed.UncompressedSize);
        Assert.All(uncompressed.Blocks, b => Assert.Null(b.CompressedSize));
        // On-wire fallback uses uncompressed size when no Size attribute present.
        Assert.Equal(131_072, uncompressed.OnWireSize);
    }

    [Fact]
    public void Rejects_Dtd()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE BlockMap [ <!ENTITY x "exploit"> ]>
            <BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap" />
            """;

        Assert.ThrowsAny<System.Xml.XmlException>(() => BlockMapParser.Parse(xml));
    }

    [Fact]
    public void Rejects_MissingHash()
    {
        var xml = """
            <BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap">
              <File Name="x.bin" Size="10" LfhSize="0">
                <Block Size="10" />
              </File>
            </BlockMap>
            """;

        Assert.Throws<InvalidOperationException>(() => BlockMapParser.Parse(xml));
    }

    [Fact]
    public void Rejects_ZeroBlockSize()
    {
        var xml = """
            <BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap">
              <File Name="x.bin" Size="10" LfhSize="0">
                <Block Hash="AAA" Size="0" />
              </File>
            </BlockMap>
            """;

        Assert.Throws<InvalidOperationException>(() => BlockMapParser.Parse(xml));
    }

    [Fact]
    public void NormalizesForwardSlashesToBackslashes()
    {
        var xml = """
            <BlockMap xmlns="http://schemas.microsoft.com/appx/2010/blockmap">
              <File Name="Assets/Logo.png" Size="100" LfhSize="0">
                <Block Hash="AAA" Size="100" />
              </File>
            </BlockMap>
            """;

        var files = BlockMapParser.Parse(xml);
        Assert.Equal(@"Assets\Logo.png", files[0].Name);
    }
}
