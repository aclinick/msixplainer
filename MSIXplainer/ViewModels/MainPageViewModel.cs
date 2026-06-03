using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.Storage.Pickers;
using MSIXplainer.Models;
using MSIXplainer.Services;

namespace MSIXplainer.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsPackageLoaded { get; set; }

    partial void OnIsPackageLoadedChanged(bool value) => RecomputeSectionsPaneVisibility();

    [ObservableProperty]
    public partial PackageInfo? PackageInfo { get; set; }

    [ObservableProperty]
    public partial string RawXml { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ManifestFinding? SelectedFinding { get; set; }

    [ObservableProperty]
    public partial string AssessmentMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial FindingSeverity OverallSeverity { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string PackageFilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedSectionTag { get; set; } = "overview";

    [ObservableProperty]
    public partial bool IsOverviewSelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSectionSelected { get; set; }

    [ObservableProperty]
    public partial bool IsRawXmlSelected { get; set; }

    public ObservableCollection<ManifestSection> Sections { get; } = [];
    public ObservableCollection<ManifestFinding> CategoryFindings { get; } = [];
    public ObservableCollection<ManifestPropertyGroup> CurrentGroups { get; } = [];
    public ObservableCollection<ManifestFinding> CriticalFindings { get; } = [];
    public ObservableCollection<ManifestFinding> WarningFindings { get; } = [];
    public ObservableCollection<ManifestFinding> ReviewFindings { get; } = [];
    public ObservableCollection<ManifestFinding> InfoFindings { get; } = [];

    /// <summary>
    /// Installed MSIX/AppX packages on this machine (issue #13). Populated lazily
    /// the first time the user expands the "Apps" nav item via <see cref="LoadInstalledAppsCommand"/>.
    /// </summary>
    public ObservableCollection<InstalledPackage> InstalledPackages { get; } = [];

    [ObservableProperty]
    public partial bool IsLoadingInstalledApps { get; set; }

    [ObservableProperty]
    public partial bool HasLoadedInstalledApps { get; set; }

    /// <summary>
    /// When true, the content area shows the Compare-Versions view (inner Frame)
    /// instead of the welcome/analysis content. Toggled by the nav rail.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCompareMode { get; set; }

    partial void OnIsCompareModeChanged(bool value) => RecomputeSectionsPaneVisibility();

    /// <summary>
    /// When true, the Apps secondary pane (Outlook-style second column) is visible
    /// between the nav rail and the main content area. Toggled by the Apps nav item.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAppsPaneOpen { get; set; }

    partial void OnIsAppsPaneOpenChanged(bool value) => RecomputeSectionsPaneVisibility();

    /// <summary>
    /// When true, the Sections secondary pane is visible in column 0 (mutually
    /// exclusive with the Apps pane). A package must be loaded and we must not
    /// be in Compare mode.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSectionsPaneVisible { get; set; }

    /// <summary>
    /// Two-way bound to the Sections ListView in the secondary pane. Changing this
    /// dispatches to <see cref="SelectSection"/>; <see cref="SelectSection"/> also
    /// writes back here so programmatic selection (e.g. "overview" on package load)
    /// highlights the right row.
    /// </summary>
    [ObservableProperty]
    public partial ManifestSection? SelectedSection { get; set; }

    partial void OnSelectedSectionChanged(ManifestSection? value)
    {
        if (value is null) return;
        if (value.Tag == SelectedSectionTag) return;
        SelectSection(value.Tag);
    }

    private void RecomputeSectionsPaneVisibility() =>
        IsSectionsPaneVisible = IsPackageLoaded && !IsCompareMode && !IsAppsPaneOpen;

    private List<ManifestFinding> _allFindings = [];
    private XElement? _manifestRoot;

    [RelayCommand]
    private async Task OpenPackageAsync()
    {
        try
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
            if (result is null) return;

            PackageFilePath = result.Path;

            if (ManifestParserService.IsBundleFile(result.Path))
            {
                var packages = ManifestParserService.ExtractFromBundle(result.Path);
                // Analyze the first package in the bundle (typically the current platform arch)
                var pkg = packages.First();
                PackageFilePath = $"{result.Path} ({pkg.Label})";
                AnalyzeManifest(pkg.RawXml, pkg.Info, pkg.Manifest);
            }
            else
            {
                var (manifest, rawXml, info) = ManifestParserService.ExtractFromPackage(result.Path);
                AnalyzeManifest(rawXml, info, manifest);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open package: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenSample()
    {
        try
        {
            var xml = SampleManifest.GetTeamsLikeManifest();
            PackageFilePath = "Sample: Contoso Collaboration Hub";
            var (manifest, rawXml, info) = ManifestParserService.ParseRawXml(xml);
            AnalyzeManifest(rawXml, info, manifest);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to load sample: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportMarkdownAsync()
    {
        if (PackageInfo is null) return;
        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(App.WindowHandle);
            var picker = new FileSavePicker(windowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"{PackageInfo.Name}-review"
            };
            picker.FileTypeChoices.Add("Markdown", [".md"]);

            var result = await picker.PickSaveFileAsync();
            if (result is null) return;

            var markdown = ExportService.ExportToMarkdown(_manifestRoot!, PackageInfo, _allFindings);
            await System.IO.File.WriteAllTextAsync(result.Path, markdown);
        }
        catch (Exception ex)
        {
            ShowError($"Export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (PackageInfo is null) return;
        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(App.WindowHandle);
            var picker = new FileSavePicker(windowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"{PackageInfo.Name}-review"
            };
            picker.FileTypeChoices.Add("JSON", [".json"]);

            var result = await picker.PickSaveFileAsync();
            if (result is null) return;

            var json = ExportService.ExportToJson(PackageInfo, _allFindings);
            await System.IO.File.WriteAllTextAsync(result.Path, json);
        }
        catch (Exception ex)
        {
            ShowError($"Export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DismissError()
    {
        HasError = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void DismissSelectedFinding() => SelectedFinding = null;

    /// <summary>
    /// Loads the list of installed MSIX/AppX packages in two passes for snappy UX:
    /// (1) fast WinRT enumeration without icons (~0.4s) → list visible immediately;
    /// (2) background icon resolution that streams each row's icon in as it resolves.
    /// Cancellable so closing the pane mid-resolution stops the work.
    /// </summary>
    [RelayCommand]
    private async Task LoadInstalledAppsAsync()
    {
        if (IsLoadingInstalledApps) return;
        if (HasLoadedInstalledApps && InstalledPackages.Count > 0) return;

        // Replace any in-flight icon-resolution loop from a previous open.
        _iconResolveCts?.Cancel();
        _iconResolveCts?.Dispose();
        _iconResolveCts = new CancellationTokenSource();
        var token = _iconResolveCts.Token;

        IsLoadingInstalledApps = true;
        try
        {
            // Pass 1: fast enumeration without icons. Returns in <1s typically.
            var packages = await Task.Run(InstalledPackageService.ListWithoutIcons, token);

            InstalledPackages.Clear();
            foreach (var p in packages)
                InstalledPackages.Add(p);

            HasLoadedInstalledApps = true;
            IsLoadingInstalledApps = false;

            // Pass 2: stream icons in. Fire-and-forget so the UI is responsive
            // immediately. Exceptions are swallowed inside ResolveIcon.
            _ = ResolveIconsAsync(token);
        }
        catch (OperationCanceledException)
        {
            IsLoadingInstalledApps = false;
        }
        catch (Exception ex)
        {
            IsLoadingInstalledApps = false;
            ShowError($"Failed to list installed packages: {ex.Message}");
        }
    }

    private async Task ResolveIconsAsync(CancellationToken token)
    {
        // Walk the collection. For each row:
        //   1. Resolve raw icon bytes on background thread (file I/O + manifest parse).
        //   2. On UI thread, decode bytes → BitmapImage with PROPER async/await
        //      (no .GetAwaiter().GetResult() — that froze the UI in the prior version).
        //   3. Replace the row with both IconBytes and a decoded IconImage set.
        for (int i = 0; i < InstalledPackages.Count; i++)
        {
            if (token.IsCancellationRequested) return;

            var current = InstalledPackages[i];
            if (current.IconImage is not null) continue;

            InstalledPackage resolved;
            try
            {
                resolved = await Task.Run(() => InstalledPackageService.ResolveIcon(current), token);
            }
            catch (OperationCanceledException) { return; }
            catch { continue; }

            if (token.IsCancellationRequested) return;
            if (resolved.IconBytes is not { Length: > 0 }) continue;

            // Decode on UI thread (we're already back on it because we awaited Task.Run).
            // Yield briefly between rows so other UI work — input, scrolling, layout —
            // gets a turn. Without this, decoding ~200 icons back-to-back can still
            // feel sluggish even though each decode is microseconds.
            var bitmap = await DecodeBitmapAsync(resolved.IconBytes);
            if (bitmap is null) continue;
            if (token.IsCancellationRequested) return;

            var idx = IndexOfByFamilyName(resolved.PackageFamilyName);
            if (idx >= 0)
                InstalledPackages[idx] = resolved with { IconImage = bitmap };

            // Cooperative yield so the UI thread can service input between rows.
            await Task.Yield();
        }
    }

    private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> DecodeBitmapAsync(byte[] bytes)
    {
        try
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private int IndexOfByFamilyName(string pfn)
    {
        for (int i = 0; i < InstalledPackages.Count; i++)
        {
            if (string.Equals(InstalledPackages[i].PackageFamilyName, pfn, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>Cancels any in-flight icon-resolution loop. Call when the Apps pane closes.</summary>
    public void CancelIconResolution()
    {
        _iconResolveCts?.Cancel();
    }

    private CancellationTokenSource? _iconResolveCts;

    /// <summary>
    /// Loads an installed package's manifest and runs it through the existing
    /// analysis pipeline. Wired to the Apps submenu items in MainPage.
    /// </summary>
    public void OpenInstalledPackage(InstalledPackage package)
    {
        if (package.ManifestPath is null || !File.Exists(package.ManifestPath))
        {
            ShowError(
                $"AppxManifest.xml not accessible at '{package.InstallLocation}'. " +
                "WindowsApps folders may require elevated access for some packages.");
            return;
        }

        try
        {
            PackageFilePath = $"Installed: {package.DisplayName} ({package.PackageFamilyName})";
            var (manifest, rawXml, info) = ManifestParserService.ExtractFromManifestFile(package.ManifestPath);
            AnalyzeManifest(rawXml, info, manifest);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to analyze installed package: {ex.Message}");
        }
    }

    public void SelectSection(string tag)
    {
        SelectedSectionTag = tag;
        IsOverviewSelected = tag == "overview";
        IsRawXmlSelected = tag == "raw-xml";
        IsSectionSelected = !IsOverviewSelected && !IsRawXmlSelected;

        // Keep the Sections pane ListView selection in sync. The setter is no-op
        // when the tag already matches (see OnSelectedSectionChanged guard).
        var match = Sections.FirstOrDefault(s => s.Tag == tag);
        if (match is not null && !ReferenceEquals(SelectedSection, match))
            SelectedSection = match;

        CurrentGroups.Clear();
        CategoryFindings.Clear();
        SelectedFinding = null;

        if (tag == "overview")
        {
            foreach (var f in _allFindings)
                CategoryFindings.Add(f);
        }
        else if (tag != "raw-xml" && _manifestRoot is not null)
        {
            var groups = ManifestExplainerService.ExplainSection(tag, _manifestRoot, _allFindings);
            foreach (var g in groups) CurrentGroups.Add(g);
        }
    }

    private void AnalyzeManifest(string rawXml, PackageInfo info, XDocument manifest)
    {
        HasError = false;
        RawXml = rawXml;
        _manifestRoot = manifest.Root!;

        _allFindings = RulesEngine.Analyze(manifest, LoadUserRuleOverrides());

        info.CriticalCount = _allFindings.Count(f => f.Severity == FindingSeverity.Critical);
        info.WarningCount = _allFindings.Count(f => f.Severity == FindingSeverity.Warning);
        info.ReviewCount = _allFindings.Count(f => f.Severity == FindingSeverity.Review);
        info.InfoCount = _allFindings.Count(f => f.Severity == FindingSeverity.Info);

        PackageInfo = info;
        IsPackageLoaded = true;

        BuildSections();
        ComputeAssessment(info);
    }

    private static RuleSeverityOverrides LoadUserRuleOverrides()
    {
        try
        {
            return File.Exists(RuleSeverityOverrides.DefaultUserPath)
                ? RuleSeverityOverrides.LoadFromFile(
                    RuleSeverityOverrides.DefaultUserPath,
                    RuleCatalog.KnownRuleIds,
                    warn: null)
                : RuleSeverityOverrides.Empty;
        }
        catch
        {
            return RuleSeverityOverrides.Empty;
        }
    }

    private void BuildSections()
    {
        Sections.Clear();

        if (_manifestRoot is null) return;

        var built = ManifestExplainerService.BuildSections(
            _manifestRoot.Document!, _allFindings, PackageInfo?.AppIconBytes);

        foreach (var s in built) Sections.Add(s);

        SelectSection("overview");
    }

    private void ComputeAssessment(PackageInfo info)
    {
        if (info.CriticalCount > 0)
        {
            OverallSeverity = FindingSeverity.Critical;
            AssessmentMessage = $"This package has {info.CriticalCount} critical finding(s) that require review before deployment. The app runs with elevated privileges or requests sensitive access.";
        }
        else if (info.WarningCount > 0)
        {
            OverallSeverity = FindingSeverity.Warning;
            AssessmentMessage = $"This package has {info.WarningCount} warning(s) worth investigating. Review the findings to understand what the app accesses.";
        }
        else if (info.ReviewCount > 0)
        {
            OverallSeverity = FindingSeverity.Review;
            AssessmentMessage = $"This package has {info.ReviewCount} item(s) to review. Overall risk is low, but verify the items match the app's stated purpose.";
        }
        else
        {
            OverallSeverity = FindingSeverity.Info;
            AssessmentMessage = "This package has a clean manifest with no significant concerns. Standard capabilities only.";
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
