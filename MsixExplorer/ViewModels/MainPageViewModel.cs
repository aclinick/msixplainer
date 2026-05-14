using System.Collections.ObjectModel;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.Storage.Pickers;
using MsixExplorer.Models;
using MsixExplorer.Services;

namespace MsixExplorer.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsPackageLoaded { get; set; }

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

    /// <summary>Raised when sections are rebuilt so code-behind can refresh NavigationView items.</summary>
    public event Action? SectionsRebuilt;

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

            var result = await picker.PickSingleFileAsync();
            if (result is null) return;

            PackageFilePath = result.Path;
            var (manifest, rawXml, info) = ManifestParserService.ExtractFromPackage(result.Path);
            AnalyzeManifest(rawXml, info, manifest);
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

    public void SelectSection(string tag)
    {
        SelectedSectionTag = tag;
        IsOverviewSelected = tag == "overview";
        IsRawXmlSelected = tag == "raw-xml";
        IsSectionSelected = !IsOverviewSelected && !IsRawXmlSelected;

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

        _allFindings = RulesEngine.Analyze(manifest);

        info.CriticalCount = _allFindings.Count(f => f.Severity == FindingSeverity.Critical);
        info.WarningCount = _allFindings.Count(f => f.Severity == FindingSeverity.Warning);
        info.ReviewCount = _allFindings.Count(f => f.Severity == FindingSeverity.Review);
        info.InfoCount = _allFindings.Count(f => f.Severity == FindingSeverity.Info);

        PackageInfo = info;
        IsPackageLoaded = true;

        BuildSections();
        ComputeAssessment(info);
        SectionsRebuilt?.Invoke();
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
