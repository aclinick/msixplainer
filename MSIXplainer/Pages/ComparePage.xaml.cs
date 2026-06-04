using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MSIXplainer.ViewModels;

namespace MSIXplainer.Pages;

public sealed partial class ComparePage : Page
{
    public ComparePageViewModel ViewModel { get; } = new();

    public ComparePage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    // ── x:Bind helper functions ──

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static string PercentToString(double value) => $"{value:F1}%";

    /// <summary>
    /// Handles selection in the sidebar tool nav (Diff / Bandwidth Planner / Duplicates).
    /// Each ListViewItem carries its tag as a string ("diff", "planner", "duplicates");
    /// we push that into the VM which flips IsXxxView visibility flags. We use
    /// SelectionChanged (not ItemClick) because ItemClick with explicit ListViewItem
    /// children returns the inner content element rather than the ListViewItem.
    /// </summary>
    private void OnCompareToolSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView list && list.SelectedItem is ListViewItem item && item.Tag is string tag)
        {
            ViewModel.SelectedCompareView = tag;
        }
    }

    /// <summary>
    /// Initialize the tool-nav ListView's selected index to match the VM after layout.
    /// Avoids the re-entrancy crash that happened when we bound each item's IsSelected
    /// via x:Bind OneWay (UIA tree probes during SelectionChanged tickled a XAML bug).
    /// </summary>
    private void OnCompareToolNavLoaded(object sender, RoutedEventArgs e)
    {
        SyncCompareToolNavSelection();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When Compare() resets SelectedCompareView to "diff", push that into the ListView.
        if (e.PropertyName == nameof(ViewModel.SelectedCompareView))
        {
            SyncCompareToolNavSelection();
        }
    }

    private void SyncCompareToolNavSelection()
    {
        var target = ViewModel.SelectedCompareView switch
        {
            "planner" => 1,
            "duplicates" => 2,
            _ => 0,
        };
        if (CompareToolNav is not null && CompareToolNav.SelectedIndex != target)
        {
            CompareToolNav.SelectedIndex = target;
        }
    }
}
