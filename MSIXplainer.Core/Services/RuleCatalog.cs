using MSIXplainer.Models;

namespace MSIXplainer.Services;

/// <summary>
/// Catalog of every rule the engine can emit, with its default severity and a
/// short human-readable description. Used by <c>rules list</c> and by the
/// override loader to detect unknown rule IDs.
/// </summary>
public static class RuleCatalog
{
    public sealed record Entry(
        string RuleId,
        FindingCategory Category,
        FindingSeverity DefaultSeverity,
        string Description);

    public static readonly IReadOnlyList<Entry> All =
    [
        // Identity
        new("identity.package", FindingCategory.Identity, FindingSeverity.Info,
            "Package identity (name, publisher, version)."),
        new("identity.simplePublisher", FindingCategory.Identity, FindingSeverity.Review,
            "Publisher certificate looks self-signed or simple (no organization fields)."),

        // Trust
        new("trust.fullTrust", FindingCategory.Trust, FindingSeverity.Info,
            "Package declares the runFullTrust capability (normal for desktop-bridge apps)."),
        new("trust.appContainer", FindingCategory.Trust, FindingSeverity.Info,
            "Package runs inside the AppContainer sandbox (uncommon, positive signal)."),
        new("trust.allowElevation", FindingCategory.Trust, FindingSeverity.Warning,
            "Package can request UAC elevation to administrator."),

        // Restricted capabilities (one rule per capability key + wildcard for unknowns)
        new("capability.broadFileSystemAccess", FindingCategory.Capabilities, FindingSeverity.Warning,
            "Read/write access to the entire user filesystem."),
        new("capability.appCaptureSettings", FindingCategory.Capabilities, FindingSeverity.Info,
            "Access to screen-capture settings."),
        new("capability.packageManagement", FindingCategory.Capabilities, FindingSeverity.Critical,
            "Can install, update, or remove other MSIX packages."),
        new("capability.appDiagnostics", FindingCategory.Capabilities, FindingSeverity.Info,
            "Can read diagnostic information about other running apps."),
        new("capability.appointmentsSystem", FindingCategory.Capabilities, FindingSeverity.Info,
            "Access to the system calendar appointments."),
        new("capability.contactsSystem", FindingCategory.Capabilities, FindingSeverity.Info,
            "Access to the system contacts database."),
        new("capability.documentsLibrary", FindingCategory.Capabilities, FindingSeverity.Info,
            "Direct access to the user's Documents library."),
        new("capability.picturesLibrary", FindingCategory.Capabilities, FindingSeverity.Review,
            "Direct access to the user's Pictures library."),
        new("capability.videosLibrary", FindingCategory.Capabilities, FindingSeverity.Review,
            "Direct access to the user's Videos library."),
        new("capability.musicLibrary", FindingCategory.Capabilities, FindingSeverity.Review,
            "Direct access to the user's Music library."),
        new("capability.removableStorage", FindingCategory.Capabilities, FindingSeverity.Info,
            "Direct access to removable storage devices."),
        new("capability.enterpriseDataPolicy", FindingCategory.Capabilities, FindingSeverity.Review,
            "Can participate in enterprise data protection (WIP) policies."),
        new("capability.inputInjectionBrokered", FindingCategory.Capabilities, FindingSeverity.Critical,
            "Can inject synthetic keyboard/mouse input into other apps."),
        new("capability.userDataTasks", FindingCategory.Capabilities, FindingSeverity.Review,
            "Access to the user's to-do tasks."),
        new("capability.smsSend", FindingCategory.Capabilities, FindingSeverity.Info,
            "Can send SMS messages."),
        new("capability.unvirtualizedResources", FindingCategory.Capabilities, FindingSeverity.Warning,
            "Requests direct (unvirtualized) access to filesystem/registry."),
        new("capability.unknownRestricted", FindingCategory.Capabilities, FindingSeverity.Review,
            "A restricted capability not in the catalog was declared."),
        new("capability.*", FindingCategory.Capabilities, FindingSeverity.Review,
            "Wildcard — overrides any capability.<name> not otherwise listed."),

        // Network / standard capabilities
        new("network.internetClient", FindingCategory.NetworkAccess, FindingSeverity.Info,
            "Outbound internet client access."),
        new("network.internetClientServer", FindingCategory.NetworkAccess, FindingSeverity.Review,
            "Outbound internet AND inbound server connections."),
        new("network.privateNetworkClientServer", FindingCategory.NetworkAccess, FindingSeverity.Review,
            "Access to devices on the local/home network."),
        new("network.vpnPlugin", FindingCategory.NetworkAccess, FindingSeverity.Critical,
            "Registers as a VPN plug-in (intercepts and routes network traffic)."),
        new("network.*", FindingCategory.NetworkAccess, FindingSeverity.Review,
            "Wildcard — overrides any network.<name> not otherwise listed."),

        // Device capabilities
        new("device.microphone", FindingCategory.DeviceAccess, FindingSeverity.Info,
            "Microphone access."),
        new("device.webcam", FindingCategory.DeviceAccess, FindingSeverity.Info,
            "Camera access."),
        new("device.location", FindingCategory.DeviceAccess, FindingSeverity.Review,
            "Geographic location access."),
        new("device.proximity", FindingCategory.DeviceAccess, FindingSeverity.Review,
            "NFC / proximity access."),
        new("device.bluetooth", FindingCategory.DeviceAccess, FindingSeverity.Review,
            "Bluetooth device access."),
        new("device.serialCommunication", FindingCategory.DeviceAccess, FindingSeverity.Info,
            "Serial (COM) port access."),
        new("device.usb", FindingCategory.DeviceAccess, FindingSeverity.Info,
            "USB device access beyond standard HID."),
        new("device.humanInterfaceDevice", FindingCategory.DeviceAccess, FindingSeverity.Review,
            "HID-class USB device access (controllers, etc.)."),
        new("device.pointOfService", FindingCategory.DeviceAccess, FindingSeverity.Review,
            "Point-of-service hardware access."),
        new("device.lowLevelDevices", FindingCategory.DeviceAccess, FindingSeverity.Info,
            "Low-level hardware buses (I2C, SPI, GPIO)."),
        new("device.gazeInput", FindingCategory.DeviceAccess, FindingSeverity.Review,
            "Eye-tracking hardware access."),
        new("device.unknown", FindingCategory.DeviceAccess, FindingSeverity.Review,
            "A device capability not in the catalog was declared."),
        new("device.*", FindingCategory.DeviceAccess, FindingSeverity.Review,
            "Wildcard — overrides any device.<name> not otherwise listed."),

        // Startup
        new("startup.task", FindingCategory.Startup, FindingSeverity.Info,
            "App registers a startup task that runs at login."),

        // Protocols / file associations
        new("protocols.handler", FindingCategory.Protocols, FindingSeverity.Review,
            "App registers a URI protocol handler (e.g. myapp://...)."),
        new("protocols.appUri", FindingCategory.Protocols, FindingSeverity.Review,
            "App registers an https://... URI handler for specific hosts."),
        new("fileAssoc.handler", FindingCategory.FileAssociations, FindingSeverity.Info,
            "App registers a file-type association (e.g. .docx)."),

        // Virtualization
        new("virt.filesystemDisabled", FindingCategory.Virtualization, FindingSeverity.Critical,
            "App actively disables MSIX filesystem write virtualization."),
        new("virt.registryDisabled", FindingCategory.Virtualization, FindingSeverity.Critical,
            "App actively disables MSIX registry write virtualization."),

        // COM
        new("com.outProcServer", FindingCategory.COM, FindingSeverity.Review,
            "App registers one or more out-of-process COM servers."),
        new("com.inProcServer", FindingCategory.COM, FindingSeverity.Info,
            "App registers in-process COM servers (shell extensions, etc.)."),

        // Background / integrations
        new("background.task", FindingCategory.BackgroundTasks, FindingSeverity.Review,
            "App registers a background task."),
        new("office.integration", FindingCategory.OfficeIntegration, FindingSeverity.Review,
            "Indicators of Office or Outlook integration."),
        new("office.extension", FindingCategory.OfficeIntegration, FindingSeverity.Review,
            "App registers an Office-specific extension."),
        new("webview2.dependency", FindingCategory.WebView2, FindingSeverity.Info,
            "App depends on the Microsoft Edge WebView2 Runtime."),

        // VDI / deployment hints
        new("vdi.perUserInstall", FindingCategory.VDI, FindingSeverity.Info,
            "Per-user installation is supported."),
        new("vdi.externalContent", FindingCategory.VDI, FindingSeverity.Review,
            "Package allows external content to be loaded."),

        // Services
        new("services.windowsService", FindingCategory.Services, FindingSeverity.Critical,
            "Installs a Windows service that runs as a background system process."),
    ];

    /// <summary>
    /// Set of all known rule IDs (including wildcards like "capability.*").
    /// </summary>
    public static readonly IReadOnlyCollection<string> KnownRuleIds =
        All.Select(e => e.RuleId).ToArray();

    public static Entry? FindEntry(string ruleId)
    {
        foreach (var entry in All)
        {
            if (string.Equals(entry.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
    }
}
