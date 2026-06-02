using MSIXplainer.Services;

namespace MSIXplainer.Tests;

public class DiffExportServiceTests
{
    [Fact]
    public void Markdown_RoundTripsHeadlineAndPackages()
    {
        // Build a minimal result with one package diff so we don't depend on disk I/O.
        var (oldFiles, newFiles) = BuildSimpleBlockMaps();
        var pkg = UpdateDiffService.DiffBlockMaps(
            label: "MyApp (x64)",
            oldVersion: "1.0.0.0",
            newVersion: "1.1.0.0",
            architecture: "x64",
            oldFiles: oldFiles,
            newFiles: newFiles);

        var result = new Models.UpdateDiffResult
        {
            OldLabel = "MyApp 1.0.0.0",
            NewLabel = "MyApp 1.1.0.0",
            PackageDiffs = [pkg],
            Warnings = ["Test warning text"]
        };

        var md = DiffExportService.ExportToMarkdown(result);
        Assert.Contains("MSIX Update Impact", md);
        Assert.Contains("MyApp 1.0.0.0", md);
        Assert.Contains("MyApp 1.1.0.0", md);
        Assert.Contains("MyApp (x64)", md);
        Assert.Contains("Test warning text", md);
        Assert.Contains("Top", md);
    }

    [Fact]
    public void Markdown_IncludesBandwidthPlanWhenProvided()
    {
        var (oldFiles, newFiles) = BuildSimpleBlockMaps();
        var pkg = UpdateDiffService.DiffBlockMaps(
            "MyApp (x64)", "1.0", "1.1", "x64", oldFiles, newFiles);
        var result = new Models.UpdateDiffResult
        {
            OldLabel = "old", NewLabel = "new", PackageDiffs = [pkg]
        };
        var bw = BandwidthPlannerService.Calculate(
            deltaBytesPerDevice: pkg.DeltaDownloadBytes,
            deviceCount: 500,
            linkSpeedsMbps: [100, 1000],
            costPerGigabyteUsd: 0.08);

        var md = DiffExportService.ExportToMarkdown(result, bw);
        Assert.Contains("Bandwidth & cost projection", md);
        Assert.Contains("500", md);
        Assert.Contains("100 Mbps", md);
        Assert.Contains("1,000 Mbps", md);
        Assert.Contains("USD", md);
    }

    [Fact]
    public void Json_IsValidAndContainsTotals()
    {
        var (oldFiles, newFiles) = BuildSimpleBlockMaps();
        var pkg = UpdateDiffService.DiffBlockMaps(
            "MyApp (x64)", "1.0", "1.1", "x64", oldFiles, newFiles);
        var result = new Models.UpdateDiffResult
        {
            OldLabel = "old", NewLabel = "new", PackageDiffs = [pkg]
        };

        var json = DiffExportService.ExportToJson(result);
        // Should parse as valid JSON.
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("Totals", out var totals));
        Assert.True(totals.TryGetProperty("DeltaDownloadBytes", out _));
        Assert.True(doc.RootElement.TryGetProperty("Packages", out _));
    }

    private static (IReadOnlyList<Models.BlockMapFile> Old, IReadOnlyList<Models.BlockMapFile> New) BuildSimpleBlockMaps()
    {
        var oldFiles = new List<Models.BlockMapFile>
        {
            new()
            {
                Name = "App.exe", UncompressedSize = 65536, LfhSize = 30,
                Blocks = new List<Models.BlockMapBlock>
                {
                    new() { Hash = "AAA", CompressedSize = 30_000, Index = 0, UncompressedSize = 65536 }
                }
            }
        };
        var newFiles = new List<Models.BlockMapFile>
        {
            new()
            {
                Name = "App.exe", UncompressedSize = 65536, LfhSize = 30,
                Blocks = new List<Models.BlockMapBlock>
                {
                    new() { Hash = "BBB", CompressedSize = 30_000, Index = 0, UncompressedSize = 65536 }
                }
            }
        };
        return (oldFiles, newFiles);
    }
}
