using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MSIXplainer.Models;
using MSIXplainer.ViewModels;
using Windows.Storage.Streams;

namespace MSIXplainer;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new();

    // Static top-of-nav items (built once in constructor); dynamic per-package section
    // items are inserted between the separator and the pane end and cleared on each rebuild.
    private NavigationViewItem? _appsItem;
    private NavigationViewItem? _openPackageItem;
    private NavigationViewItem? _compareItem;
    private NavigationViewItemSeparator? _separator;

    public MainPage()
    {
        InitializeComponent();
        ViewModel.SectionsRebuilt += RebuildNavItems;
        ViewModel.InstalledPackages.CollectionChanged += InstalledPackages_CollectionChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        BuildStaticNavItems();
    }

    private void BuildStaticNavItems()
    {
        _appsItem = new NavigationViewItem
        {
            Content = "Apps",
            Tag = "apps",
            SelectsOnInvoked = false,
            Icon = new FontIcon { Glyph = "\uE71D" } // BrowsePhotos / grid-style
        };
        AutomationProperties.SetAutomationId(_appsItem, "NavApps");

        _openPackageItem = new NavigationViewItem
        {
            Content = "Open Package…",
            Tag = "open-package",
            SelectsOnInvoked = false,
            Icon = new FontIcon { Glyph = "\uE8E5" } // OpenFile
        };
        AutomationProperties.SetAutomationId(_openPackageItem, "NavOpenPackage");

        _compareItem = new NavigationViewItem
        {
            Content = "Compare Versions…",
            Tag = "compare",
            SelectsOnInvoked = false,
            Icon = new FontIcon { Glyph = "\uE8AB" } // Switch
        };
        AutomationProperties.SetAutomationId(_compareItem, "NavCompareVersions");

        _separator = new NavigationViewItemSeparator();

        NavView.MenuItems.Add(_appsItem);
        NavView.MenuItems.Add(_openPackageItem);
        NavView.MenuItems.Add(_compareItem);
        NavView.MenuItems.Add(_separator);
    }

    private async void RebuildNavItems()
    {
        // Remove only the dynamic section items (everything AFTER our separator).
        int separatorIndex = NavView.MenuItems.IndexOf(_separator);
        if (separatorIndex < 0) return;

        for (int i = NavView.MenuItems.Count - 1; i > separatorIndex; i--)
            NavView.MenuItems.RemoveAt(i);

        NavigationViewItem? firstSection = null;

        foreach (var section in ViewModel.Sections)
        {
            var item = new NavigationViewItem
            {
                Content = section.Label,
                Tag = section.Tag,
                Icon = new FontIcon { Glyph = section.IconGlyph }
            };
            AutomationProperties.SetAutomationId(item, $"Nav_{section.Tag}");

            if (section.Tag != "overview" && section.FindingCount > 0)
            {
                item.InfoBadge = new InfoBadge { Value = section.FindingCount };
            }

            if (section.IconBytes is { Length: > 0 })
            {
                try
                {
                    var bitmap = new BitmapImage();
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(section.IconBytes);
                        await writer.StoreAsync();
                        writer.DetachStream();
                    }
                    stream.Seek(0);
                    await bitmap.SetSourceAsync(stream);
                    item.Icon = new ImageIcon { Source = bitmap, Width = 16, Height = 16 };
                }
                catch
                {
                    // Keep FontIcon fallback
                }
            }

            NavView.MenuItems.Add(item);
            firstSection ??= item;
        }

        if (firstSection is not null)
            NavView.SelectedItem = firstSection;
    }

    private void InstalledPackages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // No-op: the ListView in the Apps pane binds directly to ViewModel.InstalledPackages,
        // so collection changes propagate without a manual rebuild. Kept hooked for any
        // future side-effect (e.g. analytics) without re-wiring the constructor.
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Reserved for future cross-cutting reactions to VM property changes.
    }

    private async void NavView_Expanding(NavigationView sender, NavigationViewItemExpandingEventArgs args)
    {
        // Apps no longer expands inline (Outlook-style pane handles that). Suppress the
        // expansion and instead open the secondary pane.
        if (args.ExpandingItemContainer is NavigationViewItem nvi && nvi.Tag is "apps")
        {
            nvi.IsExpanded = false;
            await OpenAppsPaneAsync();
        }
    }

    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem invoked) return;

        switch (invoked.Tag)
        {
            case "apps":
                await OpenAppsPaneAsync();
                break;

            case "open-package":
                CloseAppsPane();
                ExitCompareMode();
                await ViewModel.OpenPackageCommand.ExecuteAsync(null);
                break;

            case "compare":
                CloseAppsPane();
                EnterCompareMode();
                break;

            case "export-markdown":
                await ViewModel.ExportMarkdownCommand.ExecuteAsync(null);
                break;

            case "export-json":
                await ViewModel.ExportJsonCommand.ExecuteAsync(null);
                break;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            // Section selection always returns to the home content (welcome/analysis).
            CloseAppsPane();
            ExitCompareMode();
            ViewModel.SelectSection(tag);
        }
    }

    private async Task OpenAppsPaneAsync()
    {
        ViewModel.IsAppsPaneOpen = true;
        if (!ViewModel.HasLoadedInstalledApps && !ViewModel.IsLoadingInstalledApps)
            await ViewModel.LoadInstalledAppsCommand.ExecuteAsync(null);
    }

    private void CloseAppsPane()
    {
        ViewModel.IsAppsPaneOpen = false;
        ViewModel.CancelIconResolution();
    }

    private void OnCloseAppsPaneClick(object sender, RoutedEventArgs e) => CloseAppsPane();

    private void OnInstalledAppClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is InstalledPackage pkg)
        {
            ExitCompareMode();
            ViewModel.OpenInstalledPackage(pkg);
        }
    }

    private void EnterCompareMode()
    {
        ViewModel.IsCompareMode = true;
        if (CompareFrame.Content is null)
            CompareFrame.Navigate(typeof(Pages.ComparePage));
    }

    internal void ExitCompareMode()
    {
        if (!ViewModel.IsCompareMode) return;
        ViewModel.IsCompareMode = false;
        CompareFrame.Content = null;
    }

    private void ViewFinding_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ManifestFinding finding)
            ViewModel.SelectedFinding = finding;
    }

    /// <summary>
    /// Copies a manifest property value to the clipboard. Wraps the clipboard
    /// call in try/catch so a transient clipboard failure (e.g. another
    /// process holding it open) never bubbles up as an unhandled exception.
    /// Bug fix for #10 — the built-in TextBlock context-menu Copy was
    /// crashing the app on some Windows builds; this gives users a reliable
    /// alternative.
    /// </summary>
    private void CopyPropertyValue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string value || string.IsNullOrEmpty(value))
            return;

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(value);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MSIXplainer] Clipboard copy failed: {ex.Message}");
        }
    }

    private void OnCompareVersionsClick(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(Pages.ComparePage));
    }

    // ── x:Bind helper functions ──

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility InvertBoolToVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NullToCollapsed(object? value) =>
        value is not null ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility StringToVisibility(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NullBytesToVisibility(byte[]? value) =>
        value is { Length: > 0 } ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NonNullBytesToVisibility(byte[]? value) =>
        value is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NullObjectToVisibility(object? value) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NonNullObjectToVisibility(object? value) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Microsoft.UI.Xaml.Media.ImageSource? ObjectToImageSource(object? value) =>
        value as Microsoft.UI.Xaml.Media.ImageSource;

    public static BitmapImage? BytesToBitmap(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var bitmap = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                writer.StoreAsync().GetAwaiter().GetResult();
                writer.DetachStream();
            }
            stream.Seek(0);
            bitmap.SetSourceAsync(stream).GetAwaiter().GetResult();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static SolidColorBrush SeverityToBrush(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => new SolidColorBrush(ColorHelper.FromArgb(255, 196, 43, 28)),
        FindingSeverity.Warning => new SolidColorBrush(ColorHelper.FromArgb(255, 157, 93, 0)),
        FindingSeverity.Review => new SolidColorBrush(ColorHelper.FromArgb(255, 0, 95, 184)),
        _ => new SolidColorBrush(ColorHelper.FromArgb(255, 96, 96, 96))
    };

    public static SolidColorBrush SeverityToBackground(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => new SolidColorBrush(ColorHelper.FromArgb(20, 196, 43, 28)),
        FindingSeverity.Warning => new SolidColorBrush(ColorHelper.FromArgb(20, 157, 93, 0)),
        FindingSeverity.Review => new SolidColorBrush(ColorHelper.FromArgb(20, 0, 95, 184)),
        _ => new SolidColorBrush(ColorHelper.FromArgb(20, 96, 96, 96))
    };

    public static InfoBarSeverity SeverityToInfoBar(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => InfoBarSeverity.Error,
        FindingSeverity.Warning => InfoBarSeverity.Warning,
        FindingSeverity.Review => InfoBarSeverity.Informational,
        _ => InfoBarSeverity.Informational
    };
}
