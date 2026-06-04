using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MSIXplainer.Services;

namespace MSIXplainer.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _initializing;

    public SettingsPage()
    {
        InitializeComponent();

        // Restore the saved theme into the RadioButtons. The SelectionChanged
        // handler must not fire during initial population — otherwise it would
        // re-save the same value and potentially fight the persisted preference.
        _initializing = true;
        var current = ThemeService.LoadPreference();
        ThemeRadios.SelectedItem = current switch
        {
            ElementTheme.Light => ThemeLightRadio,
            ElementTheme.Dark => ThemeDarkRadio,
            _ => ThemeDefaultRadio,
        };
        _initializing = false;

        VersionText.Text = GetAppVersion();
        ArchitectureText.Text = RuntimeInformation.ProcessArchitecture.ToString();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (ThemeRadios.SelectedItem is RadioButton rb && rb.Tag is string tag
            && Enum.TryParse<ElementTheme>(tag, out var theme))
        {
            ThemeService.SavePreference(theme);
            ThemeService.Apply(theme);
        }
    }

    /// <summary>
    /// Returns the assembly informational/file version (e.g. "1.0.20.0"). For
    /// packaged builds Package.ps1 stamps this via /p:Version during build.
    /// </summary>
    private static string GetAppVersion()
    {
        var asm = typeof(SettingsPage).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString() ?? "unknown";
    }
}
