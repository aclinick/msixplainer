using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.Storage.Pickers;
using MSIXplainer.Models;
using MSIXplainer.Services;

namespace MSIXplainer.ViewModels;

/// <summary>
/// Drives the Compare Versions page: takes two .msix/.appx/.msixbundle paths,
/// runs UpdateDiffService against them, and exposes the result plus an
/// interactive bandwidth/cost planner.
/// </summary>
public partial class ComparePageViewModel : ObservableObject
{
    [ObservableProperty] public partial string OldPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewPath { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasResult { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasError { get; set; }

    [ObservableProperty] public partial string OldLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewLabel { get; set; } = string.Empty;

    [ObservableProperty] public partial string FullDownloadDisplay { get; set; } = "—";
    [ObservableProperty] public partial string DeltaDownloadDisplay { get; set; } = "—";
    [ObservableProperty] public partial string SavingsDisplay { get; set; } = "—";

    // Planner inputs (TwoWay-bound).
    [ObservableProperty] public partial int DeviceCount { get; set; } = 500;
    [ObservableProperty] public partial string LinkSpeedsText { get; set; } = "100, 1000";
    [ObservableProperty] public partial double CostPerGb { get; set; } = 0;

    [ObservableProperty] public partial bool HasBandwidth { get; set; }
    [ObservableProperty] public partial string TotalTransferDisplay { get; set; } = "—";
    [ObservableProperty] public partial string EstimatedCostDisplay { get; set; } = string.Empty;

    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<string> AddedPackages { get; } = [];
    public ObservableCollection<string> RemovedPackages { get; } = [];
    public ObservableCollection<PackageDiff> PackageDiffs { get; } = [];
    public ObservableCollection<FileDiffRow> TopFileChanges { get; } = [];
    public ObservableCollection<LinkSpeedRow> LinkProjections { get; } = [];

    private UpdateDiffResult? _lastResult;
    private BandwidthEstimate? _lastBandwidth;

    [RelayCommand]
    private async Task PickOldAsync() => OldPath = await PickPackageFileAsync() ?? OldPath;

    [RelayCommand]
    private async Task PickNewAsync() => NewPath = await PickPackageFileAsync() ?? NewPath;

    [RelayCommand]
    private async Task ExportMarkdownAsync()
    {
        if (_lastResult is null) return;
        var content = DiffExportService.ExportToMarkdown(_lastResult, _lastBandwidth, topFiles: 50);
        await SaveExportAsync(content, "Markdown", ".md", "MSIXplainer-diff.md");
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (_lastResult is null) return;
        var content = DiffExportService.ExportToJson(_lastResult, _lastBandwidth);
        await SaveExportAsync(content, "JSON", ".json", "MSIXplainer-diff.json");
    }

    private static async Task SaveExportAsync(string content, string typeName, string ext, string suggestedName)
    {
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(App.WindowHandle);
        var picker = new FileSavePicker(windowId)
        {
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add(typeName, [ext]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await File.WriteAllTextAsync(file.Path, content);
    }

    [RelayCommand]
    private void Compare()
    {
        ClearError();
        if (!File.Exists(OldPath)) { SetError("Old package not found."); return; }
        if (!File.Exists(NewPath)) { SetError("New package not found."); return; }

        var oldIsBundle = ManifestParserService.IsBundleFile(OldPath);
        var newIsBundle = ManifestParserService.IsBundleFile(NewPath);
        if (oldIsBundle != newIsBundle)
        {
            SetError("Both inputs must be the same kind — either two .msix/.appx files, or two .msixbundle/.appxbundle files.");
            return;
        }

        try
        {
            IsBusy = true;
            _lastResult = oldIsBundle
                ? UpdateDiffService.CompareBundles(OldPath, NewPath)
                : UpdateDiffService.ComparePackages(OldPath, NewPath);

            PopulateFromResult(_lastResult);
            RecalculateBandwidth();
            HasResult = true;
        }
        catch (Exception ex)
        {
            HasResult = false;
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnDeviceCountChanged(int value) => RecalculateBandwidth();
    partial void OnLinkSpeedsTextChanged(string value) => RecalculateBandwidth();
    partial void OnCostPerGbChanged(double value) => RecalculateBandwidth();

    private void RecalculateBandwidth()
    {
        if (_lastResult is null) { HasBandwidth = false; _lastBandwidth = null; return; }

        var links = ParseLinkSpeeds(LinkSpeedsText);
        if (links.Count == 0 || DeviceCount < 1)
        {
            HasBandwidth = false;
            return;
        }

        try
        {
            var bw = BandwidthPlannerService.Calculate(
                deltaBytesPerDevice: _lastResult.TotalDeltaDownloadBytes,
                deviceCount: DeviceCount,
                linkSpeedsMbps: links,
                costPerGigabyteUsd: CostPerGb > 0 ? CostPerGb : null);
            _lastBandwidth = bw;

            TotalTransferDisplay = $"{Human(bw.TotalBytes)} ({bw.TotalBytes:N0} bytes)";
            EstimatedCostDisplay = bw.EstimatedCostUsd is { } c
                ? $"${c:N2} USD (at ${bw.CostPerGigabyteUsd:N3}/GB)"
                : string.Empty;

            LinkProjections.Clear();
            foreach (var lp in bw.LinkProjections)
            {
                LinkProjections.Add(new LinkSpeedRow
                {
                    Link = $"{lp.LinkSpeedMbps:N0} Mbps",
                    PerDevice = FormatDuration(lp.PerDeviceDuration),
                    SerialFleet = FormatDuration(lp.SerialFleetDuration)
                });
            }

            HasBandwidth = true;
        }
        catch
        {
            HasBandwidth = false;
        }
    }

    private void PopulateFromResult(UpdateDiffResult r)
    {
        OldLabel = r.OldLabel;
        NewLabel = r.NewLabel;

        FullDownloadDisplay = $"{Human(r.TotalFullDownloadBytes)}  ({r.TotalFullDownloadBytes:N0} bytes)";
        DeltaDownloadDisplay = $"{Human(r.TotalDeltaDownloadBytes)}  ({r.TotalDeltaDownloadBytes:N0} bytes)";
        SavingsDisplay = $"{r.SavingsPercent:F1}%";

        Warnings.Clear();
        foreach (var w in r.Warnings) Warnings.Add(w);

        AddedPackages.Clear();
        foreach (var a in r.AddedPackages) AddedPackages.Add(a);

        RemovedPackages.Clear();
        foreach (var rm in r.RemovedPackages) RemovedPackages.Add(rm);

        PackageDiffs.Clear();
        foreach (var p in r.PackageDiffs) PackageDiffs.Add(p);

        TopFileChanges.Clear();
        var top = r.PackageDiffs
            .SelectMany(p => p.Files.Select(f => new FileDiffRow
            {
                Package = p.Label,
                Path = f.Path,
                Status = f.Status.ToString(),
                Delta = Human(f.DeltaBytes),
                NewSize = Human(f.NewSize),
                OldSize = Human(f.OldSize),
                BlocksReused = $"{f.ReusedBlocks}/{f.TotalBlocks}",
                DeltaBytes = f.DeltaBytes
            }))
            .Where(row => row.DeltaBytes > 0 || row.Status is "Added" or "Removed")
            .OrderByDescending(row => row.DeltaBytes)
            .Take(50);
        foreach (var f in top) TopFileChanges.Add(f);
    }

    private static async Task<string?> PickPackageFileAsync()
    {
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(App.WindowHandle);
        var picker = new FileOpenPicker(windowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".msix");
        picker.FileTypeFilter.Add(".appx");
        picker.FileTypeFilter.Add(".msixbundle");
        picker.FileTypeFilter.Add(".appxbundle");

        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    private static List<int> ParseLinkSpeeds(string raw)
    {
        var list = new List<int>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        foreach (var token in Regex.Split(raw, "[,;\\s]+"))
        {
            if (int.TryParse(token, out var n) && n > 0) list.Add(n);
        }
        return list;
    }

    private static string Human(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        int i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
        return $"{v:F2} {units[i]}";
    }

    private static string FormatDuration(TimeSpan d)
    {
        if (d.TotalSeconds < 1) return $"{d.TotalMilliseconds:F0} ms";
        if (d.TotalSeconds < 60) return $"{d.TotalSeconds:F1} s";
        if (d.TotalMinutes < 60) return $"{d.TotalMinutes:F1} min";
        if (d.TotalHours < 24) return $"{d.TotalHours:F1} h";
        return $"{d.TotalDays:F1} d";
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }
}

public sealed class FileDiffRow
{
    public required string Package { get; init; }
    public required string Path { get; init; }
    public required string Status { get; init; }
    public required string Delta { get; init; }
    public required string NewSize { get; init; }
    public required string OldSize { get; init; }
    public required string BlocksReused { get; init; }
    public required long DeltaBytes { get; init; }
}

public sealed class LinkSpeedRow
{
    public required string Link { get; init; }
    public required string PerDevice { get; init; }
    public required string SerialFleet { get; init; }
}
