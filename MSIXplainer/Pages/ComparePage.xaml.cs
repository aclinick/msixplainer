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

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        // ComparePage is hosted in MainPage's inner CompareFrame; walking up the visual tree
        // to find MainPage lets the back button exit Compare mode cleanly without a Frame
        // navigation stack. Falls back to the outer Frame.GoBack for any other host.
        var parent = VisualTreeHelper.GetParent(this);
        while (parent is not null && parent is not MainPage)
            parent = VisualTreeHelper.GetParent(parent);

        if (parent is MainPage main)
            main.ExitCompareMode();
        else if (Frame is not null && Frame.CanGoBack)
            Frame.GoBack();
        else
            Frame?.Navigate(typeof(MainPage));
    }

    // ── x:Bind helper functions ──

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static string PercentToString(double value) => $"{value:F1}%";
}
