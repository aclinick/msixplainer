using System.Xml.Linq;
using MSIXplainer.Models;

namespace MSIXplainer.Services;

/// <summary>
/// Deterministic rules engine that analyzes an MSIX/APPX manifest and produces
/// human-readable findings for IT review. No cloud services or LLM dependencies.
/// </summary>
public static class RulesEngine
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap3 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3";
    private static readonly XNamespace Uap5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
    private static readonly XNamespace Desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
    private static readonly XNamespace Desktop6 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/6";
    private static readonly XNamespace Desktop7 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/7";
    private static readonly XNamespace Com = "http://schemas.microsoft.com/appx/manifest/com/windows10";

    public static List<ManifestFinding> Analyze(XDocument manifest)
    {
        var findings = new List<ManifestFinding>();
        var root = manifest.Root!;

        AnalyzeIdentity(root, findings);
        AnalyzeTrustLevel(root, findings);
        AnalyzeRestrictedCapabilities(root, findings);
        AnalyzeStandardCapabilities(root, findings);
        AnalyzeDeviceCapabilities(root, findings);
        AnalyzeNetworkCapabilities(root, findings);
        AnalyzeStartupTasks(root, findings);
        AnalyzeProtocolHandlers(root, findings);
        AnalyzeAppUriHandlers(root, findings);
        AnalyzeFileTypeAssociations(root, findings);
        AnalyzeVirtualization(root, findings);
        AnalyzeComRegistrations(root, findings);
        AnalyzeBackgroundTasks(root, findings);
        AnalyzeOfficeIntegration(root, findings);
        AnalyzeWebView2(root, findings);
        AnalyzeVdiIndicators(root, findings);
        AnalyzeServices(root, findings);
        AnalyzeAllowElevation(root, findings);

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Category)
            .ToList();
    }

    private static void AnalyzeIdentity(XElement root, List<ManifestFinding> findings)
    {
        var identity = root.Element(Ns + "Identity");
        if (identity is null) return;

        var name = identity.Attribute("Name")?.Value ?? "Unknown";
        var publisher = identity.Attribute("Publisher")?.Value ?? "Unknown";
        var version = identity.Attribute("Version")?.Value ?? "0.0.0.0";

        findings.Add(new ManifestFinding
        {
            Category = FindingCategory.Identity,
            Severity = FindingSeverity.Info,
            Title = "Package Identity",
            Description = $"Package \"{name}\" version {version}, published by \"{publisher}\".",
            WhyItMatters = "The package identity uniquely identifies this application in the system. The publisher CN should match your organization's trusted publisher list.",
            Recommendation = "Verify the publisher certificate (CN) matches the expected organization. Check the version against known-good versions.",
            XmlSnippet = identity.ToString()
        });

        if (publisher.Contains("CN=", StringComparison.OrdinalIgnoreCase)
            && !publisher.Contains("O=", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.Identity,
                Severity = FindingSeverity.Review,
                Title = "Self-signed or simple publisher certificate",
                Description = "The publisher certificate uses only a CN (Common Name) without an Organization (O=) field, which may indicate a self-signed or developer certificate.",
                WhyItMatters = "Production apps from established vendors typically use EV or OV certificates with full organization details. Self-signed certificates are normal for development but unusual in production.",
                Recommendation = "Confirm this is expected. For enterprise deployment, verify the certificate chain and trust.",
                XmlSnippet = $"Publisher=\"{publisher}\""
            });
        }
    }

    private static void AnalyzeTrustLevel(XElement root, List<ManifestFinding> findings)
    {
        var capabilities = GetAllCapabilityNames(root);

        if (capabilities.Contains("runFullTrust"))
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.Trust,
                Severity = FindingSeverity.Info,
                Title = "Runs with Full Trust",
                Description = "This application declares the runFullTrust capability. It runs with the same permissions as a traditional desktop (Win32) application — outside the AppContainer sandbox.",
                WhyItMatters = "This is the normal configuration for desktop apps packaged as MSIX (the \"desktop bridge\"). The vast majority of line-of-business and productivity apps use full trust. By itself, this is expected and not a concern — the meaningful signal is in the specific capabilities, extensions, and file/registry virtualization findings below.",
                Recommendation = "Review the other Trust and Capabilities findings to understand the app's actual surface area. The presence of full trust alone is normal for a desktop app.",
                XmlSnippet = "<rescap:Capability Name=\"runFullTrust\" />"
            });
        }
        else
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.Trust,
                Severity = FindingSeverity.Info,
                Title = "Runs in AppContainer sandbox",
                Description = "This application runs inside the AppContainer sandbox with limited permissions. It can only access resources explicitly granted through declared capabilities.",
                WhyItMatters = "Sandboxed apps are safer by default. They cannot access arbitrary files, registry keys, or system resources unless the manifest explicitly requests them. This is uncommon for desktop-bridge MSIX packages — most repackaged Win32 apps require full trust — so seeing AppContainer here is a positive sign.",
                Recommendation = "Good security posture. Review the declared capabilities to ensure they are appropriate.",
                XmlSnippet = "<!-- No runFullTrust capability declared -->"
            });
        }
    }

    private static void AnalyzeAllowElevation(XElement root, List<ManifestFinding> findings)
    {
        var allowElevation = FindElementByLocalName(root, "allowElevation");
        if (allowElevation is not null)
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.Trust,
                Severity = FindingSeverity.Warning,
                Title = "Can request Admin elevation (UAC)",
                Description = "This application declares the allowElevation capability, meaning it can prompt for administrator privileges via UAC.",
                WhyItMatters = "An elevated app has full system-level access — it can modify protected system files, install drivers, change security settings, and access other users' data. This is the highest privilege level on Windows.",
                Recommendation = "Verify this is absolutely necessary. Most line-of-business apps should not need elevation. If required, ensure the app is from a trusted vendor with a verified certificate.",
                XmlSnippet = allowElevation.ToString()
            });
        }
    }

    private static void AnalyzeRestrictedCapabilities(XElement root, List<ManifestFinding> findings)
    {
        var capabilities = GetAllCapabilityNames(root);

        var restrictedCapabilities = new Dictionary<string, (FindingSeverity Severity, string Description, string WhyItMatters, string Recommendation)>
        {
            ["broadFileSystemAccess"] = (FindingSeverity.Warning,
                "Can access the entire user filesystem including Documents, Desktop, Downloads, and other personal folders.",
                "This grants read/write access well beyond the app's own data. The app can read sensitive documents, modify files in any user-accessible location, or exfiltrate data.",
                "Verify the app genuinely needs broad file access. Most apps should use specific folder pickers instead. Check if the vendor explains why this capability is needed."),

            ["appCaptureSettings"] = (FindingSeverity.Info,
                "Can access screen capture settings and potentially initiate screen recordings.",
                "Screen capture can expose sensitive information displayed on screen, including passwords, confidential documents, and private communications.",
                "Verify this is expected behavior for the app type (e.g., screen sharing, recording tools)."),

            ["packageManagement"] = (FindingSeverity.Critical,
                "Can install, remove, and manage other application packages on the system.",
                "This capability allows the app to modify what software is installed. A compromised app could install malware or remove security tools.",
                "This is very unusual for most apps. Verify the vendor and use case. Only deployment and management tools should need this."),

            ["appDiagnostics"] = (FindingSeverity.Info,
                "Can access diagnostic information about other running apps, including process name, memory usage, and CPU time.",
                "This allows the app to survey what other software is running on the system, which could be used for reconnaissance.",
                "Typically needed by system monitoring or diagnostic tools. Verify this matches the app's purpose."),

            ["appointmentsSystem"] = (FindingSeverity.Info,
                "Can read, create, and modify calendar appointments in the system calendar.",
                "Calendar data often contains sensitive meeting details, attendee lists, and location information.",
                "Expected for calendar and productivity apps. Verify the app needs calendar integration."),

            ["contactsSystem"] = (FindingSeverity.Info,
                "Can read and modify the system contacts database.",
                "Contact data contains personally identifiable information (PII) including names, emails, phone numbers, and addresses.",
                "Expected for communication and CRM apps. Verify the app needs contact access."),

            ["documentsLibrary"] = (FindingSeverity.Info,
                "Can access the user's Documents library directly without a file picker prompt.",
                "Unlike broadFileSystemAccess, this is scoped to Documents only, but still provides silent access without user consent per file.",
                "Verify the app needs direct document access. File picker-based access is safer as the user explicitly selects files."),

            ["picturesLibrary"] = (FindingSeverity.Review,
                "Can access the user's Pictures library directly.",
                "Photos can contain EXIF metadata with GPS coordinates, timestamps, and device information.",
                "Expected for photo editing and gallery apps. Verify the app type."),

            ["videosLibrary"] = (FindingSeverity.Review,
                "Can access the user's Videos library directly.",
                "Similar to Pictures — videos may contain personal content. Direct library access bypasses user file selection.",
                "Expected for video editing and media apps."),

            ["musicLibrary"] = (FindingSeverity.Review,
                "Can access the user's Music library directly.",
                "Music library access is lower risk but still provides direct filesystem access without per-file user consent.",
                "Expected for music players and audio apps."),

            ["removableStorage"] = (FindingSeverity.Info,
                "Can access files on removable storage devices (USB drives, SD cards) directly.",
                "Removable media may contain sensitive files. The app can also write to removable storage, which could be used for data exfiltration.",
                "Verify the app needs direct removable storage access. Consider if file picker access would suffice."),

            ["enterpriseDataPolicy"] = (FindingSeverity.Review,
                "Participates in Windows Information Protection (WIP) enterprise data policies.",
                "This indicates the app is designed to handle enterprise-managed data and will respect WIP policies for data separation.",
                "Positive indicator for enterprise environments — the app is WIP-aware."),

            ["inputInjectionBrokered"] = (FindingSeverity.Critical,
                "Can inject input events (keystrokes, mouse clicks) into other applications.",
                "Input injection can be used to automate actions in other apps, bypass security dialogs, or simulate user interaction without consent.",
                "This is extremely powerful. Only accessibility tools and automation frameworks should need this. Verify carefully."),

            ["userDataTasks"] = (FindingSeverity.Review,
                "Can access user tasks and to-do items from the system task provider.",
                "Task data may contain personal and work-related action items.",
                "Expected for productivity and task management apps."),

            ["smsSend"] = (FindingSeverity.Info,
                "Can send SMS messages from the device.",
                "SMS sending could be used for premium-rate messaging or for sending data to external services without user awareness.",
                "Unusual for most desktop apps. Verify this capability matches the app's purpose."),

            ["unvirtualizedResources"] = (FindingSeverity.Warning,
                "Requests direct access to unvirtualized filesystem and registry resources, bypassing MSIX container isolation.",
                "This negates some of the security benefits of MSIX packaging by allowing direct writes to the real filesystem and registry.",
                "Check if the app also disables filesystem or registry virtualization. This combination removes key MSIX isolation benefits."),
        };

        foreach (var cap in capabilities)
        {
            if (restrictedCapabilities.TryGetValue(cap, out var info))
            {
                findings.Add(new ManifestFinding
                {
                    Category = FindingCategory.Capabilities,
                    Severity = info.Severity,
                    Title = $"Restricted capability: {cap}",
                    Description = info.Description,
                    WhyItMatters = info.WhyItMatters,
                    Recommendation = info.Recommendation,
                    XmlSnippet = $"<rescap:Capability Name=\"{cap}\" />"
                });
            }
        }

        // Flag any unknown restricted capabilities
        var allRestricted = root.Descendants()
            .Where(e => e.Name.Namespace == Rescap && e.Name.LocalName == "Capability")
            .Select(e => e.Attribute("Name")?.Value)
            .Where(v => v is not null && v != "runFullTrust" && !restrictedCapabilities.ContainsKey(v!))
            .Distinct();

        foreach (var cap in allRestricted)
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.Capabilities,
                Severity = FindingSeverity.Review,
                Title = $"Restricted capability: {cap}",
                Description = $"This app declares the restricted capability \"{cap}\". This capability requires Microsoft approval for Store submission.",
                WhyItMatters = "Restricted capabilities grant access to sensitive resources that most apps don't need. The specific impact depends on the capability.",
                Recommendation = $"Research what \"{cap}\" grants. Restricted capabilities should be documented by the vendor.",
                XmlSnippet = $"<rescap:Capability Name=\"{cap}\" />"
            });
        }
    }

    private static void AnalyzeStandardCapabilities(XElement root, List<ManifestFinding> findings)
    {
        var capabilities = GetAllCapabilityNames(root);

        var standardCaps = new Dictionary<string, (string Description, string WhyItMatters)>
        {
            ["internetClient"] = (
                "Can make outbound internet connections (HTTP/HTTPS, WebSocket).",
                "The app can send data to external servers. Normal for most modern apps but verify the vendor is trustworthy."),
            ["internetClientServer"] = (
                "Can make outbound internet connections AND accept inbound connections as a server.",
                "The app can listen for incoming network connections, making the machine a network endpoint. This could expose the system to remote connections."),
            ["privateNetworkClientServer"] = (
                "Can access devices on the local/home network, both as client and server.",
                "The app can discover and communicate with other devices on the LAN, including printers, IoT devices, and other computers. In enterprise networks, this could allow lateral movement."),
        };

        foreach (var cap in capabilities)
        {
            if (standardCaps.TryGetValue(cap, out var info))
            {
                var severity = cap == "internetClient" ? FindingSeverity.Info : FindingSeverity.Review;
                findings.Add(new ManifestFinding
                {
                    Category = FindingCategory.NetworkAccess,
                    Severity = severity,
                    Title = $"Network: {cap}",
                    Description = info.Description,
                    WhyItMatters = info.WhyItMatters,
                    Recommendation = cap == "internetClient"
                        ? "Standard for most apps. No action needed unless the app should be fully offline."
                        : "Verify the app needs this level of network access. Consider firewall rules to restrict scope.",
                    XmlSnippet = $"<Capability Name=\"{cap}\" />"
                });
            }
        }
    }

    private static void AnalyzeDeviceCapabilities(XElement root, List<ManifestFinding> findings)
    {
        var deviceCaps = new Dictionary<string, (FindingSeverity Severity, string Description, string WhyItMatters)>
        {
            ["microphone"] = (FindingSeverity.Info,
                "Can access the device microphone for audio recording.",
                "Microphone access enables audio surveillance if the app is compromised. Verify the app legitimately needs audio input."),
            ["webcam"] = (FindingSeverity.Info,
                "Can access the device camera for photo/video capture.",
                "Camera access enables visual surveillance. Verify the app needs camera functionality."),
            ["location"] = (FindingSeverity.Review,
                "Can access the device's geographic location.",
                "Location data reveals user whereabouts. May be needed for mapping or location-based features."),
            ["proximity"] = (FindingSeverity.Review,
                "Can use near-field communication (NFC) with nearby devices.",
                "NFC access allows short-range wireless communication, typically for pairing or data exchange."),
            ["bluetooth"] = (FindingSeverity.Review,
                "Can access Bluetooth functionality to communicate with nearby devices.",
                "Bluetooth can be used to discover and communicate with nearby devices, peripherals, and beacons."),
            ["serialCommunication"] = (FindingSeverity.Info,
                "Can access serial (COM) ports for hardware communication.",
                "Serial port access allows direct communication with connected hardware, which could include industrial control systems."),
            ["usb"] = (FindingSeverity.Info,
                "Can access USB devices beyond standard HID peripherals.",
                "Direct USB access can interact with specialized hardware and could potentially access or modify connected storage."),
            ["humanInterfaceDevice"] = (FindingSeverity.Review,
                "Can access HID-class USB devices (joysticks, game controllers, etc.).",
                "HID access is typical for apps that work with specialized input devices."),
            ["pointOfService"] = (FindingSeverity.Review,
                "Can access point-of-service devices (barcode scanners, receipt printers).",
                "Point-of-service access is typical for retail and logistics applications."),
            ["lowLevelDevices"] = (FindingSeverity.Info,
                "Can access low-level hardware buses (I2C, SPI, GPIO).",
                "Low-level hardware access provides direct control over system buses, which is powerful and potentially dangerous on shared machines."),
            ["gazeInput"] = (FindingSeverity.Review,
                "Can access eye-tracking hardware for gaze-based input.",
                "Gaze tracking reveals what the user is looking at on screen."),
        };

        var declaredDeviceCaps = root.Descendants()
            .Where(e => e.Name.LocalName == "DeviceCapability")
            .Select(e => e.Attribute("Name")?.Value)
            .Where(v => v is not null)
            .Distinct();

        foreach (var cap in declaredDeviceCaps)
        {
            if (deviceCaps.TryGetValue(cap!, out var info))
            {
                findings.Add(new ManifestFinding
                {
                    Category = FindingCategory.DeviceAccess,
                    Severity = info.Severity,
                    Title = $"Device access: {cap}",
                    Description = info.Description,
                    WhyItMatters = info.WhyItMatters,
                    Recommendation = "Verify this device access matches the app's stated purpose.",
                    XmlSnippet = $"<DeviceCapability Name=\"{cap}\" />"
                });
            }
            else
            {
                findings.Add(new ManifestFinding
                {
                    Category = FindingCategory.DeviceAccess,
                    Severity = FindingSeverity.Review,
                    Title = $"Device access: {cap}",
                    Description = $"This app requests access to \"{cap}\" hardware capabilities.",
                    WhyItMatters = "Device capabilities grant direct hardware access. Review whether this matches the app's purpose.",
                    Recommendation = $"Research what \"{cap}\" provides and verify it is needed.",
                    XmlSnippet = $"<DeviceCapability Name=\"{cap}\" />"
                });
            }
        }
    }

    private static void AnalyzeNetworkCapabilities(XElement root, List<ManifestFinding> findings)
    {
        // Already handled in AnalyzeStandardCapabilities for the main ones.
        // Here we look for specific network-related extensions.
        var vpnExtensions = FindExtensionsByCategory(root, "windows.vpnPlugIn");
        foreach (var ext in vpnExtensions)
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.NetworkAccess,
                Severity = FindingSeverity.Critical,
                Title = "VPN plug-in registration",
                Description = "This app registers as a VPN plug-in, which allows it to create virtual network interfaces and route network traffic.",
                WhyItMatters = "A VPN plug-in can intercept, inspect, and modify all network traffic on the device. This is extremely powerful and should only be granted to trusted VPN providers.",
                Recommendation = "Verify this is a legitimate VPN application from your approved vendor list.",
                XmlSnippet = ext.ToString()
            });
        }
    }

    private static void AnalyzeStartupTasks(XElement root, List<ManifestFinding> findings)
    {
        var startupTasks = FindExtensionsByCategory(root, "windows.startupTask");
        foreach (var task in startupTasks)
        {
            var taskId = task.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "StartupTask")
                ?.Attribute("TaskId")?.Value ?? "unknown";
            var enabled = task.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "StartupTask")
                ?.Attribute("Enabled")?.Value ?? "true";

            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.Startup,
                Severity = FindingSeverity.Info,
                Title = $"Auto-starts at login: {taskId}",
                Description = $"This app registers a startup task (ID: \"{taskId}\") that launches automatically when the user signs in. Default enabled: {enabled}.",
                WhyItMatters = "Startup tasks increase login time, consume system resources, and maintain a persistent presence on the machine. Multiple startup tasks compound this effect across all installed apps.",
                Recommendation = "Determine if auto-start is necessary. Users can disable this in Task Manager → Startup, but the app may re-enable it. Consider group policy to manage startup apps.",
                XmlSnippet = task.ToString()
            });
        }
    }

    private static void AnalyzeProtocolHandlers(XElement root, List<ManifestFinding> findings)
    {
        var protocols = root.Descendants()
            .Where(e => e.Name.LocalName == "Protocol" && e.Attribute("Name") is not null);

        foreach (var proto in protocols)
        {
            var name = proto.Attribute("Name")?.Value ?? "unknown";
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.Protocols,
                Severity = FindingSeverity.Review,
                Title = $"Protocol handler: {name}://",
                Description = $"This app registers to handle the \"{name}://\" URI protocol. When any app, browser, or email opens a link starting with \"{name}://\", this application will be launched.",
                WhyItMatters = "Protocol handlers can be invoked from web pages, emails, or other apps without additional user consent. A malicious or crafted link could launch this app with attacker-controlled parameters.",
                Recommendation = "Review what the app does when launched via this protocol. Ensure the app validates protocol parameters and doesn't blindly execute commands from URI arguments.",
                XmlSnippet = proto.Parent?.ToString() ?? proto.ToString()
            });
        }
    }

    private static void AnalyzeAppUriHandlers(XElement root, List<ManifestFinding> findings)
    {
        var uriHandlers = root.Descendants()
            .Where(e => e.Name.LocalName == "AppUriHandler");

        foreach (var handler in uriHandlers)
        {
            var hosts = handler.Descendants()
                .Where(e => e.Name.LocalName == "Host")
                .Select(e => e.Attribute("Name")?.Value)
                .Where(h => h is not null);

            var hostList = string.Join(", ", hosts);
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.Protocols,
                Severity = FindingSeverity.Review,
                Title = $"App URI handler: {hostList}",
                Description = $"This app registers to handle web URIs for: {hostList}. When a user clicks a link to these domains, the app can intercept and handle it instead of the browser.",
                WhyItMatters = "App URI handlers redirect web navigation to native code. Verify the app legitimately owns these domains. The domains must have a JSON validation file, but this should still be reviewed.",
                Recommendation = "Confirm the listed domains are owned by the app vendor. Check that the domains serve the required windows-app-web-link JSON validation file.",
                XmlSnippet = handler.Parent?.ToString() ?? handler.ToString()
            });
        }
    }

    private static void AnalyzeFileTypeAssociations(XElement root, List<ManifestFinding> findings)
    {
        var ftaExtensions = root.Descendants()
            .Where(e => e.Name.LocalName == "FileTypeAssociation");

        foreach (var fta in ftaExtensions)
        {
            var name = fta.Attribute("Name")?.Value ?? "unknown";
            var types = fta.Descendants()
                .Where(e => e.Name.LocalName == "FileType")
                .Select(e => e.Value)
                .ToList();

            var typeList = types.Count > 0 ? string.Join(", ", types) : "none listed";
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.FileAssociations,
                Severity = FindingSeverity.Info,
                Title = $"File association: {typeList}",
                Description = $"This app registers to open files with extensions: {typeList}. Double-clicking these file types will offer to open them with this app.",
                WhyItMatters = "File type associations change the default handler for those file types. If the app handles executable or script formats, it could be a vector for launching untrusted content.",
                Recommendation = "Verify the file types match the app's purpose. Be cautious if the app handles script or executable formats (.ps1, .bat, .exe, .dll).",
                XmlSnippet = fta.Parent?.ToString() ?? fta.ToString()
            });
        }
    }

    private static void AnalyzeVirtualization(XElement root, List<ManifestFinding> findings)
    {
        var fsVirt = FindElementByLocalName(root, "FileSystemWriteVirtualization");
        if (fsVirt is not null)
        {
            var enabled = !string.Equals(fsVirt.Value?.Trim(), "disabled", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fsVirt.Attribute("Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase);

            if (!enabled)
            {
                findings.Add(new ManifestFinding
                {
                    Category = FindingCategory.Virtualization,
                    Severity = FindingSeverity.Critical,
                    Title = "Filesystem virtualization DISABLED",
                    Description = "This app disables MSIX filesystem write virtualization. File writes go directly to the real filesystem instead of being redirected to the app's virtual container.",
                    WhyItMatters = "Filesystem virtualization is a key MSIX isolation feature. When disabled, the app's file writes persist after uninstall and could modify system or user files directly.",
                    Recommendation = "Verify the app needs direct filesystem writes. This means uninstalling the app may leave files behind.",
                    XmlSnippet = fsVirt.Parent?.ToString() ?? fsVirt.ToString()
                });
            }
        }

        var regVirt = FindElementByLocalName(root, "RegistryWriteVirtualization");
        if (regVirt is not null)
        {
            var enabled = !string.Equals(regVirt.Value?.Trim(), "disabled", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(regVirt.Attribute("Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase);

            if (!enabled)
            {
                findings.Add(new ManifestFinding
                {
                    Category = FindingCategory.Virtualization,
                    Severity = FindingSeverity.Critical,
                    Title = "Registry virtualization DISABLED",
                    Description = "This app disables MSIX registry write virtualization. Registry writes go directly to the real registry instead of being redirected to the app's virtual hive.",
                    WhyItMatters = "Registry virtualization prevents apps from modifying the real registry. When disabled, the app can write persistent registry keys that survive uninstallation.",
                    Recommendation = "Verify the app needs direct registry access. This could leave registry artifacts after uninstall.",
                    XmlSnippet = regVirt.Parent?.ToString() ?? regVirt.ToString()
                });
            }
        }
    }

    private static void AnalyzeComRegistrations(XElement root, List<ManifestFinding> findings)
    {
        var comServers = root.Descendants()
            .Where(e => e.Name.LocalName is "ExeServer" or "SurrogateServer" or "ComServer");

        var serverList = comServers.ToList();
        if (serverList.Count > 0)
        {
            var clsids = root.Descendants()
                .Where(e => e.Name.LocalName == "Class")
                .Select(e => e.Attribute("Id")?.Value)
                .Where(v => v is not null)
                .ToList();

            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.COM,
                Severity = FindingSeverity.Review,
                Title = $"COM server registration ({clsids.Count} class{(clsids.Count != 1 ? "es" : "")})",
                Description = $"This app registers {clsids.Count} COM class(es) that other applications can invoke. COM servers run code in response to requests from other apps on the system.",
                WhyItMatters = "COM registrations create inter-process communication endpoints. Other applications (including potentially malicious ones) could instantiate these COM objects. This also indicates deeper system integration.",
                Recommendation = "Review the registered CLSIDs and their purpose. COM servers are common for apps with Office integration, shell extensions, or inter-app communication.",
                XmlSnippet = serverList.First().ToString()
            });
        }

        // In-process COM servers (shell extensions, thumbnail handlers, etc.)
        var inprocServers = root.Descendants()
            .Where(e => e.Name.LocalName == "InProcessServer");

        if (inprocServers.Any())
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.COM,
                Severity = FindingSeverity.Info,
                Title = "In-process COM server (shell extension)",
                Description = "This app registers in-process COM servers that load as DLLs inside other processes (like Explorer). This is commonly used for shell extensions, context menu handlers, or thumbnail providers.",
                WhyItMatters = "In-process servers run inside the host process's address space. A buggy or malicious in-process server could crash Explorer or other host processes, and has full access to the host process's memory.",
                Recommendation = "Verify the in-process servers are for expected shell integration. Monitor for Explorer stability issues.",
                XmlSnippet = inprocServers.First().ToString()
            });
        }
    }

    private static void AnalyzeBackgroundTasks(XElement root, List<ManifestFinding> findings)
    {
        var bgTasks = FindExtensionsByCategory(root, "windows.backgroundTasks");
        foreach (var task in bgTasks)
        {
            var taskTypes = task.Descendants()
                .Where(e => e.Name.LocalName == "Task")
                .Select(e => e.Attribute("Type")?.Value)
                .Where(v => v is not null);

            var types = string.Join(", ", taskTypes);
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.BackgroundTasks,
                Severity = FindingSeverity.Review,
                Title = $"Background task: {(string.IsNullOrEmpty(types) ? "general" : types)}",
                Description = $"This app registers background tasks that can run even when the app's main window is not visible. Task types: {(string.IsNullOrEmpty(types) ? "not specified" : types)}.",
                WhyItMatters = "Background tasks consume system resources (CPU, memory, battery) and can perform actions without user awareness. They can send network requests, access files, and process data in the background.",
                Recommendation = "Review what the background tasks do. Consider battery and performance impact, especially on laptops.",
                XmlSnippet = task.ToString()
            });
        }
    }

    private static void AnalyzeOfficeIntegration(XElement root, List<ManifestFinding> findings)
    {
        var xmlString = root.ToString();

        if (xmlString.Contains("OfficeApp", StringComparison.OrdinalIgnoreCase) ||
            xmlString.Contains("MailAppVersion", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.OfficeIntegration,
                Severity = FindingSeverity.Review,
                Title = "Office/Outlook integration detected",
                Description = "This package contains indicators of Microsoft Office or Outlook integration. The app may install add-ins, task panes, or mail app extensions.",
                WhyItMatters = "Office integrations run inside Office processes and can access document content, email bodies, and contact information. They extend the attack surface of Office applications.",
                Recommendation = "Verify the Office integration is expected. Review Office admin center for add-in deployment policies.",
                XmlSnippet = "<!-- Office integration indicators found in manifest -->"
            });
        }

        // Check for desktop:Extension with specific Office-related categories
        var officeExtensions = root.Descendants()
            .Where(e => e.Name.LocalName == "Extension"
                && (e.Attribute("Category")?.Value?.Contains("office", StringComparison.OrdinalIgnoreCase) == true
                    || e.Attribute("Category")?.Value?.Contains("outlook", StringComparison.OrdinalIgnoreCase) == true));

        foreach (var ext in officeExtensions)
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.OfficeIntegration,
                Severity = FindingSeverity.Review,
                Title = $"Office extension: {ext.Attribute("Category")?.Value}",
                Description = "This app registers an Office-specific extension that integrates with Microsoft Office applications.",
                WhyItMatters = "Office extensions have access to document content and user data within Office apps.",
                Recommendation = "Review the specific extension type and verify it matches the app's purpose.",
                XmlSnippet = ext.ToString()
            });
        }
    }

    private static void AnalyzeWebView2(XElement root, List<ManifestFinding> findings)
    {
        var xmlString = root.ToString();
        var deps = root.Element(Ns + "Dependencies");

        var hasWebView2Dep = deps?.Elements()
            .Any(e => e.Attribute("Name")?.Value?.Contains("WebView2", StringComparison.OrdinalIgnoreCase) == true
                    || e.Attribute("Name")?.Value?.Contains("Microsoft.Web.WebView2", StringComparison.OrdinalIgnoreCase) == true) == true;

        var hasWebView2Ref = xmlString.Contains("WebView2", StringComparison.OrdinalIgnoreCase)
            || xmlString.Contains("EdgeWebView", StringComparison.OrdinalIgnoreCase);

        if (hasWebView2Dep || hasWebView2Ref)
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.WebView2,
                Severity = FindingSeverity.Info,
                Title = "WebView2 runtime dependency",
                Description = "This app uses the WebView2 control (Chromium-based) to display web content. It depends on the Microsoft Edge WebView2 Runtime being installed.",
                WhyItMatters = "WebView2 apps render web content inside a native window. The web content could access local resources if the app grants permissions. Ensure the WebView2 Runtime is kept up to date for security patches.",
                Recommendation = "Verify WebView2 Runtime is deployed and auto-updated in your environment. Review whether the app loads remote web content or only local resources.",
                XmlSnippet = "<!-- WebView2 dependency detected -->"
            });
        }
    }

    private static void AnalyzeVdiIndicators(XElement root, List<ManifestFinding> findings)
    {
        var xmlString = root.ToString();

        var perUserInstall = FindElementByLocalName(root, "PerUserInstallation");
        if (perUserInstall is not null)
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.VDI,
                Severity = FindingSeverity.Info,
                Title = "Per-user installation support",
                Description = "This package supports per-user installation, which is relevant for non-admin deployments and VDI/multi-user environments.",
                WhyItMatters = "Per-user installs don't require admin rights and are isolated per user profile. This is generally positive for VDI and shared machine scenarios.",
                Recommendation = "Good for VDI environments. Verify the app works correctly in multi-session environments if applicable.",
                XmlSnippet = perUserInstall.ToString()
            });
        }

        if (xmlString.Contains("allowExternalContent", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.VDI,
                Severity = FindingSeverity.Review,
                Title = "Allows external content",
                Description = "This package declares allowExternalContent, meaning it can reference content (files, resources) from outside its package directory.",
                WhyItMatters = "External content references mean the app loads resources from locations outside the immutable MSIX package. This could be used to update app behavior without repackaging.",
                Recommendation = "Verify where external content is loaded from. Ensure the external locations are controlled and trusted.",
                XmlSnippet = "<!-- allowExternalContent detected -->"
            });
        }
    }

    private static void AnalyzeServices(XElement root, List<ManifestFinding> findings)
    {
        var services = root.Descendants()
            .Where(e => e.Name.LocalName == "Service"
                || (e.Name.LocalName == "Extension"
                    && e.Attribute("Category")?.Value == "windows.service"));

        foreach (var svc in services)
        {
            var name = svc.Attribute("Name")?.Value
                ?? svc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Service")?.Attribute("Name")?.Value
                ?? "unknown";

            findings.Add(new ManifestFinding
            {
                Category = FindingCategory.Services,
                Severity = FindingSeverity.Critical,
                Title = $"Windows service: {name}",
                Description = $"This app installs a Windows service named \"{name}\" that runs in the background as a system-level process.",
                WhyItMatters = "Windows services run independently of user sessions, often with elevated privileges, and persist across reboots. They are a significant trust decision.",
                Recommendation = "Verify the service is necessary. Check what account the service runs under and what resources it accesses.",
                XmlSnippet = svc.ToString()
            });
        }
    }

    // --- Helper methods ---

    private static HashSet<string> GetAllCapabilityNames(XElement root)
    {
        return root.Descendants()
            .Where(e => e.Name.LocalName is "Capability" or "DeviceCapability")
            .Select(e => e.Attribute("Name")?.Value)
            .Where(v => v is not null)
            .ToHashSet()!;
    }

    private static List<XElement> FindExtensionsByCategory(XElement root, string category)
    {
        return root.Descendants()
            .Where(e => e.Name.LocalName == "Extension"
                && e.Attribute("Category")?.Value == category)
            .ToList();
    }

    private static XElement? FindElementByLocalName(XElement root, string localName)
    {
        return root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == localName);
    }
}
