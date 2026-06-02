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

    public MainPage()
    {
        InitializeComponent();
        ViewModel.SectionsRebuilt += RebuildNavItems;
    }

    private async void RebuildNavItems()
    {
        NavView.MenuItems.Clear();
        NavigationViewItem? firstItem = null;

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

            // Load app icon from package if available
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
            firstItem ??= item;
        }

        if (firstItem is not null)
            NavView.SelectedItem = firstItem;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            ViewModel.SelectSection(tag);
        }
    }

    private void ViewFinding_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ManifestFinding finding)
            ViewModel.SelectedFinding = finding;
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
