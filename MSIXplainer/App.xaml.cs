using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MSIXplainer;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// Global safety net for UI-thread exceptions. Prevents process termination
    /// when an unexpected exception bubbles up — for example, WinUI 3's built-in
    /// TextBlock context-menu Copy command has been observed to throw
    /// <see cref="System.Runtime.InteropServices.COMException"/> on some
    /// Windows builds when the clipboard is contended. Catching it here keeps
    /// the analysis session alive; the failed action is simply a no-op.
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[MSIXplainer] Unhandled UI exception swallowed: {e.Exception}");
        e.Handled = true;
    }

    /// <summary>
    /// File path provided via file activation (right-click → Open with
    /// MSIXplainer). MainPage reads this on construction and loads the
    /// package. Null when the app was launched normally.
    /// </summary>
    public static string? PendingFileActivationPath { get; private set; }

    /// <summary>
    /// Clears the pending file-activation path. Call after the path has been
    /// loaded so it isn't re-applied on subsequent page reloads.
    /// </summary>
    public static void ConsumePendingFileActivationPath() => PendingFileActivationPath = null;

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Capture file-activation arg BEFORE MainWindow is constructed so
        // MainPage can pick it up during its own construction. See #20.
        PendingFileActivationPath = TryGetFileActivationPath();

        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
        // Apply persisted theme after Window.Content is set so the FrameworkElement
        // root exists. ThemeService.Apply no-ops if Content is still null.
        MSIXplainer.Services.ThemeService.Apply(MSIXplainer.Services.ThemeService.LoadPreference());
    }

    /// <summary>
    /// If the app was launched via a file activation, return the first file's
    /// path; otherwise return null. Safe to call early in OnLaunched.
    /// </summary>
    private static string? TryGetFileActivationPath()
    {
        try
        {
            var activated = Microsoft.Windows.AppLifecycle.AppInstance
                .GetCurrent()
                .GetActivatedEventArgs();

            System.Diagnostics.Debug.WriteLine(
                $"[MSIXplainer] Activation kind: {activated.Kind}");

            if (activated.Kind != Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File)
                return null;

            if (activated.Data is not Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MSIXplainer] Activation data is {activated.Data?.GetType().FullName ?? "null"}, not IFileActivatedEventArgs");
                return null;
            }

            var firstFile = fileArgs.Files.FirstOrDefault();
            System.Diagnostics.Debug.WriteLine(
                $"[MSIXplainer] File activation path: {firstFile?.Path ?? "<null>"}");
            return firstFile?.Path;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MSIXplainer] File activation failed: {ex.Message}");
            return null;
        }
    }
}
