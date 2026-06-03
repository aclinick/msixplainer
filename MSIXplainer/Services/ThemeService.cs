using Microsoft.UI.Xaml;
using Windows.Storage;

namespace MSIXplainer.Services;

/// <summary>
/// Persists the user's preferred app theme (Light / Dark / System default) to
/// local app settings and applies it to the live window content. The chosen
/// theme is re-applied on every launch from <see cref="App.OnLaunched"/>.
/// </summary>
public static class ThemeService
{
    private const string SettingsKey = "AppTheme";

    public static ElementTheme LoadPreference()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(SettingsKey, out var raw)
                && raw is string s
                && Enum.TryParse<ElementTheme>(s, ignoreCase: true, out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // App may be running unpackaged in tests; fall through to default.
        }
        return ElementTheme.Default;
    }

    public static void SavePreference(ElementTheme theme)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[SettingsKey] = theme.ToString();
        }
        catch
        {
            // Best-effort; unpackaged contexts have no LocalSettings.
        }
    }

    /// <summary>
    /// Applies <paramref name="theme"/> to the active window's root content.
    /// Safe to call before the window exists (no-op in that case).
    /// </summary>
    public static void Apply(ElementTheme theme)
    {
        if (App.Window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
    }
}
