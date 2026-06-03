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
    }

    // ── x:Bind helper functions ──

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static string PercentToString(double value) => $"{value:F1}%";
}
