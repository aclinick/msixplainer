using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace MSIXplainer;

public static class Program
{
    // [STAThread] is REQUIRED. We disable XamlGeneratedMain
    // (DISABLE_XAML_GENERATED_MAIN in the .csproj) and hand-write Main, so we
    // have to set the apartment ourselves. With [MTAThread] the app appears
    // to work — XAML, bindings, navigation, even file pickers all run — but
    // WinRT OLE-backed APIs like Windows.ApplicationModel.DataTransfer.Clipboard
    // throw CO_E_NOTINITIALIZED (0x800401F0). See issue #21.
    [STAThread]
    static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();
        Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
