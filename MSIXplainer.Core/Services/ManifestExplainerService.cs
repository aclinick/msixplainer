using System.Xml.Linq;
using MSIXplainer.Models;

namespace MSIXplainer.Services;

/// <summary>
/// Builds explained property groups from manifest XML for each section.
/// Every element and attribute gets a plain-English explanation.
/// Findings from the RulesEngine are linked inline where applicable.
/// </summary>
public static class ManifestExplainerService
{
    internal static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
    private static readonly XNamespace Com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
    private static readonly XNamespace Mp = "http://schemas.microsoft.com/appx/2014/phone/manifest";

    // ────────────────────────────────────────────────────────────────
    //  Section discovery — build NavigationView items from XML
    // ────────────────────────────────────────────────────────────────

    public static List<ManifestSection> BuildSections(XDocument manifest, List<ManifestFinding> findings, byte[]? appIconBytes = null)
    {
        var root = manifest.Root!;
        var sections = new List<ManifestSection>
        {
            new()
            {
                Tag = "overview",
                Label = "Overview",
                IconGlyph = "\uE80F",
                FindingCount = findings.Count,
                WorstSeverity = findings.Count > 0 ? findings.Max(f => f.Severity) : FindingSeverity.Info
            }
        };

        if (root.Element(Ns + "Identity") is not null)
            sections.Add(MakeSection("identity", "Identity", "\uE77B", findings, FindingCategory.Identity));

        if (root.Elements(Ns + "Properties").Any())
            sections.Add(MakeSection("properties", "Properties", "\uE8A1", findings, FindingCategory.Virtualization));

        if (root.Element(Ns + "Dependencies") is not null)
            sections.Add(new ManifestSection { Tag = "dependencies", Label = "Dependencies", IconGlyph = "\uE74C" });

        if (root.Element(Ns + "Resources") is not null)
            sections.Add(new ManifestSection { Tag = "resources", Label = "Resources", IconGlyph = "\uE774" });

        var apps = root.Element(Ns + "Applications")?.Elements(Ns + "Application") ?? [];
        foreach (var app in apps)
        {
            var appId = app.Attribute("Id")?.Value ?? "App";
            var displayName = app.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "VisualElements")
                ?.Attribute("DisplayName")?.Value ?? appId;

            var appCategories = new[] {
                FindingCategory.Trust, FindingCategory.Startup, FindingCategory.Protocols,
                FindingCategory.FileAssociations, FindingCategory.BackgroundTasks,
                FindingCategory.COM, FindingCategory.OfficeIntegration, FindingCategory.WebView2
            };
            var appFindings = findings.Where(f => appCategories.Contains(f.Category)).ToList();

            sections.Add(new ManifestSection
            {
                Tag = $"app:{appId}",
                Label = displayName,
                IconGlyph = "\uE737",
                IconBytes = appIconBytes,
                FindingCount = appFindings.Count,
                WorstSeverity = appFindings.Count > 0 ? appFindings.Max(f => f.Severity) : FindingSeverity.Info
            });
        }

        if (root.Element(Ns + "Capabilities") is not null)
        {
            var capCategories = new[] {
                FindingCategory.Capabilities, FindingCategory.DeviceAccess, FindingCategory.NetworkAccess
            };
            var capFindings = findings.Where(f => capCategories.Contains(f.Category)).ToList();
            sections.Add(new ManifestSection
            {
                Tag = "capabilities",
                Label = "Capabilities",
                IconGlyph = "\uE8D7",
                FindingCount = capFindings.Count,
                WorstSeverity = capFindings.Count > 0 ? capFindings.Max(f => f.Severity) : FindingSeverity.Info
            });
        }

        return sections;
    }

    // ────────────────────────────────────────────────────────────────
    //  Section dispatch — route tag to the right explainer
    // ────────────────────────────────────────────────────────────────

    public static List<ManifestPropertyGroup> ExplainSection(string tag, XElement root, List<ManifestFinding> findings)
    {
        if (tag == "identity") return ExplainIdentity(root, findings);
        if (tag == "properties") return ExplainProperties(root, findings);
        if (tag == "dependencies") return ExplainDependencies(root);
        if (tag == "resources") return ExplainResources(root);
        if (tag == "capabilities") return ExplainCapabilities(root, findings);
        if (tag.StartsWith("app:"))
        {
            var appId = tag[4..];
            var app = root.Descendants(Ns + "Application")
                .FirstOrDefault(a => a.Attribute("Id")?.Value == appId);
            return app is not null ? ExplainApplication(app, findings) : [];
        }
        return [];
    }

    // ────────────────────────────────────────────────────────────────
    //  Identity
    // ────────────────────────────────────────────────────────────────

    private static List<ManifestPropertyGroup> ExplainIdentity(XElement root, List<ManifestFinding> findings)
    {
        var groups = new List<ManifestPropertyGroup>();
        var identity = root.Element(Ns + "Identity");
        if (identity is null) return groups;

        var identityFindings = findings.Where(f => f.Category == FindingCategory.Identity).ToList();

        groups.Add(new ManifestPropertyGroup
        {
            Header = "Package Identity",
            Description = "Uniquely identifies this package in the Windows ecosystem. Verified against the signing certificate at install time.",
            IconGlyph = "\uE77B",
            Properties =
            [
                new ManifestProperty
                {
                    Label = "Name",
                    Value = identity.Attribute("Name")?.Value ?? "Not specified",
                    Explanation = "The unique package family name. Windows uses this to track installations, updates, and data isolation. Format is typically 'Publisher.AppName'.",
                    Finding = FindFinding(identityFindings, "Package Identity")
                },
                new ManifestProperty
                {
                    Label = "Publisher",
                    Value = identity.Attribute("Publisher")?.Value ?? "Not specified",
                    Explanation = "The X.509 certificate subject name (Distinguished Name). Must exactly match the signing certificate. Check against your organization's trusted publisher list.",
                    Finding = FindFinding(identityFindings, "publisher", "certificate")
                },
                new ManifestProperty
                {
                    Label = "Version",
                    Value = identity.Attribute("Version")?.Value ?? "0.0.0.0",
                    Explanation = "Package version in Major.Minor.Build.Revision format. Windows uses this for update detection — higher versions replace lower ones from the same publisher."
                },
                new ManifestProperty
                {
                    Label = "Architecture",
                    Value = identity.Attribute("ProcessorArchitecture")?.Value ?? "neutral",
                    Explanation = "Target processor architecture (x86, x64, arm64, or neutral). Ensure this matches your deployment targets."
                }
            ]
        });

        var phoneId = root.Element(Mp + "PhoneIdentity");
        if (phoneId is not null)
        {
            groups.Add(new ManifestPropertyGroup
            {
                Header = "Phone Identity (Legacy)",
                Description = "Legacy Windows Phone / Mobile identifier. Present for backward compatibility in packages that predate the unified platform.",
                IconGlyph = "\uE8EA",
                Properties =
                [
                    new ManifestProperty
                    {
                        Label = "PhoneProductId",
                        Value = phoneId.Attribute("PhoneProductId")?.Value ?? "Not specified",
                        Explanation = "Legacy Windows Phone product GUID. No longer used on desktop."
                    },
                    new ManifestProperty
                    {
                        Label = "PhonePublisherId",
                        Value = phoneId.Attribute("PhonePublisherId")?.Value ?? "Not specified",
                        Explanation = "Legacy publisher GUID. Typically all zeros for desktop-only packages."
                    }
                ]
            });
        }

        return groups;
    }

    // ────────────────────────────────────────────────────────────────
    //  Properties
    // ────────────────────────────────────────────────────────────────

    private static List<ManifestPropertyGroup> ExplainProperties(XElement root, List<ManifestFinding> findings)
    {
        var groups = new List<ManifestPropertyGroup>();
        var allProps = root.Elements(Ns + "Properties").ToList();
        if (allProps.Count == 0) return groups;

        // Display properties
        var display = allProps[0];
        var displayGroup = new ManifestPropertyGroup
        {
            Header = "Display Information",
            Description = "How the app appears in the Start menu, Settings, and Microsoft Store.",
            IconGlyph = "\uE8A1",
            Properties = []
        };
        AddChildProperty(displayGroup.Properties, display, Ns + "DisplayName", "Display Name",
            "The app name shown in the Start menu, taskbar, and Settings → Apps.");
        AddChildProperty(displayGroup.Properties, display, Ns + "PublisherDisplayName", "Publisher Display Name",
            "Friendly publisher name shown to users. Not the certificate CN — this is the marketing name.");
        AddChildProperty(displayGroup.Properties, display, Ns + "Logo", "Store Logo",
            "Path to the icon used in the Microsoft Store listing and app installer.");
        AddChildProperty(displayGroup.Properties, display, Ns + "Description", "Description",
            "App description shown in Settings and the Store.");
        if (displayGroup.Properties.Count > 0) groups.Add(displayGroup);

        // Virtualization settings (may be in any Properties element)
        var virtFindings = findings.Where(f => f.Category == FindingCategory.Virtualization).ToList();
        var virtProps = new List<ManifestProperty>();
        foreach (var propElement in allProps)
        {
            var fsVirt = propElement.Elements().FirstOrDefault(e => e.Name.LocalName == "FileSystemWriteVirtualization");
            if (fsVirt is not null)
            {
                virtProps.Add(new ManifestProperty
                {
                    Label = "Filesystem Virtualization",
                    Value = fsVirt.Value.Trim(),
                    Explanation = "Controls whether file writes are redirected to a virtual container. 'enabled' (default) isolates writes; 'disabled' writes directly to the real filesystem.",
                    Finding = FindFinding(virtFindings, "Filesystem")
                });
            }
            var regVirt = propElement.Elements().FirstOrDefault(e => e.Name.LocalName == "RegistryWriteVirtualization");
            if (regVirt is not null)
            {
                virtProps.Add(new ManifestProperty
                {
                    Label = "Registry Virtualization",
                    Value = regVirt.Value.Trim(),
                    Explanation = "Controls whether registry writes are redirected to a virtual hive. 'enabled' (default) isolates writes; 'disabled' writes to the real registry.",
                    Finding = FindFinding(virtFindings, "Registry")
                });
            }
        }
        if (virtProps.Count > 0)
        {
            groups.Add(new ManifestPropertyGroup
            {
                Header = "Virtualization Settings",
                Description = "MSIX container isolation. Virtualization redirects filesystem and registry writes to prevent apps from modifying system state directly.",
                IconGlyph = "\uE8F1",
                Properties = virtProps
            });
        }

        return groups;
    }

    // ────────────────────────────────────────────────────────────────
    //  Dependencies
    // ────────────────────────────────────────────────────────────────

    private static List<ManifestPropertyGroup> ExplainDependencies(XElement root)
    {
        var groups = new List<ManifestPropertyGroup>();
        var deps = root.Element(Ns + "Dependencies");
        if (deps is null) return groups;

        var families = deps.Elements().Where(e => e.Name.LocalName == "TargetDeviceFamily").ToList();
        if (families.Count > 0)
        {
            groups.Add(new ManifestPropertyGroup
            {
                Header = "Target Platform",
                Description = "Which Windows device families and OS versions this package supports.",
                IconGlyph = "\uE770",
                Properties = families.Select(f => new ManifestProperty
                {
                    Label = f.Attribute("Name")?.Value ?? "Unknown",
                    Value = $"Min: {f.Attribute("MinVersion")?.Value ?? "?"} — Max tested: {f.Attribute("MaxVersionTested")?.Value ?? "?"}",
                    Explanation = ExplainDeviceFamily(f.Attribute("Name")?.Value, f.Attribute("MinVersion")?.Value)
                }).ToList()
            });
        }

        var pkgDeps = deps.Elements().Where(e => e.Name.LocalName == "PackageDependency").ToList();
        if (pkgDeps.Count > 0)
        {
            groups.Add(new ManifestPropertyGroup
            {
                Header = "Package Dependencies",
                Description = "Framework packages required at runtime. These are typically installed automatically by the Store or app installer.",
                IconGlyph = "\uE74C",
                Properties = pkgDeps.Select(d => new ManifestProperty
                {
                    Label = d.Attribute("Name")?.Value ?? "Unknown",
                    Value = $"Min version: {d.Attribute("MinVersion")?.Value ?? "?"}",
                    Explanation = ExplainPackageDependency(d.Attribute("Name")?.Value)
                }).ToList()
            });
        }

        return groups;
    }

    // ────────────────────────────────────────────────────────────────
    //  Resources
    // ────────────────────────────────────────────────────────────────

    private static List<ManifestPropertyGroup> ExplainResources(XElement root)
    {
        var groups = new List<ManifestPropertyGroup>();
        var resources = root.Element(Ns + "Resources");
        if (resources is null) return groups;

        var items = resources.Elements().ToList();
        if (items.Count == 0) return groups;

        var props = items.Select(r =>
        {
            var lang = r.Attribute("Language")?.Value;
            var scale = r.Attribute("Scale")?.Value;
            if (lang is not null)
                return new ManifestProperty
                {
                    Label = lang,
                    Value = "Language",
                    Explanation = $"Localized resources for {ExplainLanguageCode(lang)}."
                };
            if (scale is not null)
                return new ManifestProperty
                {
                    Label = $"{scale}%",
                    Value = "Display Scale",
                    Explanation = $"Assets optimized for {scale}% display scaling."
                };
            return new ManifestProperty
            {
                Label = r.Name.LocalName,
                Value = r.Value,
                Explanation = "A resource qualifier for this package."
            };
        }).ToList();

        groups.Add(new ManifestPropertyGroup
        {
            Header = "Package Resources",
            Description = "Languages, display scales, and hardware qualifiers. More languages indicate wider international deployment.",
            IconGlyph = "\uE774",
            Properties = props
        });

        return groups;
    }

    // ────────────────────────────────────────────────────────────────
    //  Application
    // ────────────────────────────────────────────────────────────────

    private static List<ManifestPropertyGroup> ExplainApplication(XElement app, List<ManifestFinding> findings)
    {
        var groups = new List<ManifestPropertyGroup>();

        // Entry point
        var entryPoint = app.Attribute("EntryPoint")?.Value ?? "";
        var executable = app.Attribute("Executable")?.Value ?? "";
        var appId = app.Attribute("Id")?.Value ?? "";
        var isFullTrust = entryPoint == "Windows.FullTrustApplication";

        groups.Add(new ManifestPropertyGroup
        {
            Header = "Entry Point",
            Description = "How Windows launches this application. The entry point determines the app's security context.",
            IconGlyph = "\uE7E8",
            Properties =
            [
                new ManifestProperty
                {
                    Label = "Application Id",
                    Value = appId,
                    Explanation = "Internal identifier for this application within the package. A package can contain multiple applications."
                },
                new ManifestProperty
                {
                    Label = "Executable",
                    Value = executable,
                    Explanation = "The main executable launched when the app starts. Located within the package install directory."
                },
                new ManifestProperty
                {
                    Label = "Entry Point",
                    Value = entryPoint,
                    Explanation = isFullTrust
                        ? "Runs as a full-trust Win32 application with unrestricted access. NOT sandboxed in AppContainer."
                        : $"Managed entry point class. The app runs in a sandboxed AppContainer environment.",
                    Finding = FindFinding(findings, FindingCategory.Trust, "Full Trust")
                }
            ]
        });

        // Visual Elements
        var ve = app.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
        if (ve is not null)
        {
            var veProps = new List<ManifestProperty>();
            AddAttrProperty(veProps, ve, "DisplayName", "Display Name",
                "App name shown in Start menu tile and taskbar.");
            AddAttrProperty(veProps, ve, "Description", "Description",
                "Tooltip text shown when hovering over the Start menu tile.");
            AddAttrProperty(veProps, ve, "BackgroundColor", "Tile Background",
                "Background color for the Start menu tile. 'transparent' uses the system accent color.");
            AddAttrProperty(veProps, ve, "Square150x150Logo", "Medium Tile",
                "150×150 pixel tile image for the medium Start menu layout.");
            AddAttrProperty(veProps, ve, "Square44x44Logo", "Small Icon",
                "44×44 pixel icon for taskbar, notifications, and small tiles.");

            var dt = ve.Elements().FirstOrDefault(e => e.Name.LocalName == "DefaultTile");
            if (dt is not null)
            {
                AddAttrProperty(veProps, dt, "Wide310x150Logo", "Wide Tile", "310×150 wide tile image.");
                AddAttrProperty(veProps, dt, "Square310x310Logo", "Large Tile", "310×310 large tile image.");
                AddAttrProperty(veProps, dt, "ShortName", "Short Name", "Abbreviated tile label.");
            }

            if (veProps.Count > 0)
            {
                groups.Add(new ManifestPropertyGroup
                {
                    Header = "Visual Elements",
                    Description = "Start menu tiles, icons, and visual branding. Controls how the app appears in the Windows shell.",
                    IconGlyph = "\uE790",
                    Properties = veProps
                });
            }
        }

        // Extensions — one group per extension
        var extensions = app.Elements().FirstOrDefault(e => e.Name.LocalName == "Extensions");
        if (extensions is not null)
        {
            foreach (var ext in extensions.Elements().Where(e => e.Name.LocalName == "Extension"))
            {
                var category = ext.Attribute("Category")?.Value ?? "";
                var group = ExplainExtension(ext, category, findings);
                if (group is not null) groups.Add(group);
            }
        }

        return groups;
    }

    // ────────────────────────────────────────────────────────────────
    //  Capabilities
    // ────────────────────────────────────────────────────────────────

    private static List<ManifestPropertyGroup> ExplainCapabilities(XElement root, List<ManifestFinding> findings)
    {
        var groups = new List<ManifestPropertyGroup>();
        var capsElement = root.Element(Ns + "Capabilities");
        if (capsElement is null) return groups;

        // Restricted capabilities
        var restrictedCaps = capsElement.Elements()
            .Where(e => e.Name.Namespace == Rescap && e.Name.LocalName == "Capability")
            .ToList();
        if (restrictedCaps.Count > 0)
        {
            groups.Add(new ManifestPropertyGroup
            {
                Header = "Restricted Capabilities",
                Description = "Require Microsoft approval for Store submission. These grant access to sensitive system resources.",
                IconGlyph = "\uE72E",
                Properties = restrictedCaps.Select(c =>
                {
                    var name = c.Attribute("Name")?.Value ?? "unknown";
                    return new ManifestProperty
                    {
                        Label = name,
                        Value = "Restricted",
                        Explanation = ExplainRestrictedCapability(name),
                        Finding = FindFinding(findings,
                            [FindingCategory.Capabilities, FindingCategory.Trust], name)
                    };
                }).ToList()
            });
        }

        // Standard capabilities
        var standardCaps = capsElement.Elements()
            .Where(e => e.Name.Namespace == Ns && e.Name.LocalName == "Capability")
            .ToList();
        if (standardCaps.Count > 0)
        {
            groups.Add(new ManifestPropertyGroup
            {
                Header = "Standard Capabilities",
                Description = "Common capabilities available to all packaged apps without special approval.",
                IconGlyph = "\uE8D7",
                Properties = standardCaps.Select(c =>
                {
                    var name = c.Attribute("Name")?.Value ?? "unknown";
                    return new ManifestProperty
                    {
                        Label = name,
                        Value = "Standard",
                        Explanation = ExplainStandardCapability(name),
                        Finding = FindFinding(findings,
                            [FindingCategory.NetworkAccess, FindingCategory.Capabilities], name)
                    };
                }).ToList()
            });
        }

        // Device capabilities
        var deviceCaps = capsElement.Elements()
            .Where(e => e.Name.LocalName == "DeviceCapability")
            .ToList();
        if (deviceCaps.Count > 0)
        {
            groups.Add(new ManifestPropertyGroup
            {
                Header = "Device Capabilities",
                Description = "Hardware access (cameras, microphones, sensors). Each requires user consent at runtime.",
                IconGlyph = "\uE772",
                Properties = deviceCaps.Select(c =>
                {
                    var name = c.Attribute("Name")?.Value ?? "unknown";
                    return new ManifestProperty
                    {
                        Label = name,
                        Value = "Device",
                        Explanation = ExplainDeviceCapability(name),
                        Finding = FindFinding(findings, [FindingCategory.DeviceAccess], name)
                    };
                }).ToList()
            });
        }

        return groups;
    }

    // ────────────────────────────────────────────────────────────────
    //  Extension explainers
    // ────────────────────────────────────────────────────────────────

    private static ManifestPropertyGroup? ExplainExtension(XElement ext, string category, List<ManifestFinding> findings) => category switch
    {
        "windows.startupTask" => ExplainStartupTask(ext, findings),
        "windows.protocol" => ExplainProtocol(ext, findings),
        "windows.appUriHandler" => ExplainAppUriHandler(ext, findings),
        "windows.fileTypeAssociation" => ExplainFileTypeAssociation(ext, findings),
        "windows.backgroundTasks" => ExplainBackgroundTasks(ext, findings),
        "windows.comServer" => ExplainComServer(ext, findings),
        _ => ExplainGenericExtension(ext, category)
    };

    private static ManifestPropertyGroup ExplainStartupTask(XElement ext, List<ManifestFinding> findings)
    {
        var task = ext.Descendants().FirstOrDefault(e => e.Name.LocalName == "StartupTask");
        var taskId = task?.Attribute("TaskId")?.Value ?? "unknown";
        var enabled = task?.Attribute("Enabled")?.Value ?? "true";
        var displayName = task?.Attribute("DisplayName")?.Value ?? "";

        return new ManifestPropertyGroup
        {
            Header = $"Startup Task: {taskId}",
            Description = "Registers this app to launch automatically when the user signs in.",
            IconGlyph = "\uE7E8",
            Properties =
            [
                new ManifestProperty
                {
                    Label = "Task ID",
                    Value = taskId,
                    Explanation = "Internal startup task identifier. Visible in Task Manager → Startup.",
                    Finding = FindFinding(findings, FindingCategory.Startup, taskId)
                },
                new ManifestProperty
                {
                    Label = "Enabled by Default",
                    Value = enabled,
                    Explanation = enabled.Equals("true", StringComparison.OrdinalIgnoreCase)
                        ? "Auto-starts at login unless the user disables it in Task Manager."
                        : "Registered but disabled by default. The app may enable it later."
                },
                new ManifestProperty
                {
                    Label = "Display Name",
                    Value = displayName,
                    Explanation = "Name shown in Task Manager's Startup tab."
                }
            ]
        };
    }

    private static ManifestPropertyGroup ExplainProtocol(XElement ext, List<ManifestFinding> findings)
    {
        var protocol = ext.Descendants().FirstOrDefault(e => e.Name.LocalName == "Protocol");
        var name = protocol?.Attribute("Name")?.Value ?? "unknown";
        var displayName = protocol?.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value ?? "";

        return new ManifestPropertyGroup
        {
            Header = $"Protocol: {name}://",
            Description = $"Handles the {name}:// URI scheme. Links with this prefix launch this app.",
            IconGlyph = "\uE71B",
            Properties =
            [
                new ManifestProperty
                {
                    Label = "Protocol Name",
                    Value = $"{name}://",
                    Explanation = "The URI scheme this app handles. Any link, app, or script using this URI invokes this application.",
                    Finding = FindFinding(findings, FindingCategory.Protocols, name)
                },
                new ManifestProperty
                {
                    Label = "Display Name",
                    Value = displayName,
                    Explanation = "Friendly name shown when Windows asks which app to use for this protocol."
                }
            ]
        };
    }

    private static ManifestPropertyGroup ExplainAppUriHandler(XElement ext, List<ManifestFinding> findings)
    {
        var handler = ext.Descendants().FirstOrDefault(e => e.Name.LocalName == "AppUriHandler");
        var hosts = handler?.Descendants()
            .Where(e => e.Name.LocalName == "Host")
            .Select(e => e.Attribute("Name")?.Value ?? "")
            .ToList() ?? [];

        var hostList = string.Join(", ", hosts);
        var props = hosts.Select(h => new ManifestProperty
        {
            Label = h,
            Value = "Web Domain",
            Explanation = $"Links to {h} are intercepted by this app instead of the browser. The domain must serve a windows-app-web-link JSON validation file."
        }).ToList();

        if (props.Count > 0)
            props[0].Finding = FindFinding(findings, FindingCategory.Protocols, "URI handler");

        return new ManifestPropertyGroup
        {
            Header = "App URI Handler",
            Description = $"Intercepts web links for: {hostList}. Redirects browser navigation to native app.",
            IconGlyph = "\uE71B",
            Properties = props
        };
    }

    private static ManifestPropertyGroup ExplainFileTypeAssociation(XElement ext, List<ManifestFinding> findings)
    {
        var fta = ext.Descendants().FirstOrDefault(e => e.Name.LocalName == "FileTypeAssociation");
        var name = fta?.Attribute("Name")?.Value ?? "unknown";
        var types = fta?.Descendants()
            .Where(e => e.Name.LocalName == "FileType")
            .Select(e => e.Value).ToList() ?? [];
        var typeList = string.Join(", ", types);

        var props = types.Select(t => new ManifestProperty
        {
            Label = t,
            Value = "File Extension",
            Explanation = $"Double-clicking a {t} file offers to open it with this app."
        }).ToList();

        if (props.Count > 0)
            props[0].Finding = FindFinding(findings, FindingCategory.FileAssociations, typeList);

        return new ManifestPropertyGroup
        {
            Header = $"File Association: {typeList}",
            Description = "Registers as a handler for these file types. Changes what happens on double-click.",
            IconGlyph = "\uE8A5",
            Properties = props
        };
    }

    private static ManifestPropertyGroup ExplainBackgroundTasks(XElement ext, List<ManifestFinding> findings)
    {
        var bg = ext.Descendants().FirstOrDefault(e => e.Name.LocalName == "BackgroundTasks");
        var tasks = bg?.Elements()
            .Where(e => e.Name.LocalName == "Task")
            .Select(e => e.Attribute("Type")?.Value ?? "unknown").ToList() ?? [];
        var entryPoint = ext.Attribute("EntryPoint")?.Value ?? "";

        var props = new List<ManifestProperty>();
        if (!string.IsNullOrEmpty(entryPoint))
        {
            props.Add(new ManifestProperty
            {
                Label = "Entry Point",
                Value = entryPoint,
                Explanation = "The code entry point that handles background activation.",
                Finding = FindFinding(findings, FindingCategory.BackgroundTasks, "Background")
            });
        }
        props.AddRange(tasks.Select(t => new ManifestProperty
        {
            Label = t,
            Value = "Task Type",
            Explanation = ExplainBackgroundTaskType(t)
        }));

        return new ManifestPropertyGroup
        {
            Header = "Background Tasks",
            Description = "Runs code in the background when the app window is not visible — on push notifications, timers, or system events.",
            IconGlyph = "\uE823",
            Properties = props
        };
    }

    private static ManifestPropertyGroup ExplainComServer(XElement ext, List<ManifestFinding> findings)
    {
        var comServer = ext.Descendants().FirstOrDefault(e => e.Name.LocalName == "ComServer");
        var exeServers = comServer?.Descendants().Where(e => e.Name.LocalName == "ExeServer").ToList() ?? [];
        var props = new List<ManifestProperty>();

        foreach (var server in exeServers)
        {
            var exe = server.Attribute("Executable")?.Value ?? "";
            var dn = server.Attribute("DisplayName")?.Value ?? "";
            props.Add(new ManifestProperty
            {
                Label = dn,
                Value = exe,
                Explanation = "COM out-of-process server. Other applications (like Office) can invoke this via COM."
            });
            foreach (var cls in server.Elements().Where(e => e.Name.LocalName == "Class"))
            {
                props.Add(new ManifestProperty
                {
                    Label = $"  CLSID: {cls.Attribute("DisplayName")?.Value ?? ""}",
                    Value = cls.Attribute("Id")?.Value ?? "",
                    Explanation = "COM class ID (CLSID). Other apps create instances of this class via CoCreateInstance."
                });
            }
        }

        if (props.Count > 0)
            props[0].Finding = FindFinding(findings, FindingCategory.COM, "COM");

        return new ManifestPropertyGroup
        {
            Header = "COM Server Registration",
            Description = "Registers COM servers for inter-process communication. Commonly used for Office add-ins, shell extensions, and legacy interop.",
            IconGlyph = "\uE943",
            Properties = props
        };
    }

    private static ManifestPropertyGroup ExplainGenericExtension(XElement ext, string category)
    {
        var props = new List<ManifestProperty>
        {
            new() { Label = "Category", Value = category, Explanation = "Extension type. Refer to Microsoft documentation for details." }
        };
        foreach (var attr in ext.Attributes().Where(a => a.Name.LocalName != "Category"))
            props.Add(new ManifestProperty { Label = attr.Name.LocalName, Value = attr.Value, Explanation = $"Extension attribute." });

        return new ManifestPropertyGroup
        {
            Header = $"Extension: {category}",
            Description = $"An application extension of type '{category}'.",
            IconGlyph = "\uE9CE",
            Properties = props
        };
    }

    // ────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────

    private static ManifestSection MakeSection(string tag, string label, string glyph,
        List<ManifestFinding> findings, params FindingCategory[] categories)
    {
        var matched = findings.Where(f => categories.Contains(f.Category)).ToList();
        return new ManifestSection
        {
            Tag = tag,
            Label = label,
            IconGlyph = glyph,
            FindingCount = matched.Count,
            WorstSeverity = matched.Count > 0 ? matched.Max(f => f.Severity) : FindingSeverity.Info
        };
    }

    private static ManifestFinding? FindFinding(List<ManifestFinding> findings, params string[] keywords) =>
        findings.FirstOrDefault(f =>
            keywords.Any(k => f.Title.Contains(k, StringComparison.OrdinalIgnoreCase)));

    private static ManifestFinding? FindFinding(List<ManifestFinding> findings, FindingCategory category, string keyword) =>
        findings.FirstOrDefault(f =>
            f.Category == category &&
            f.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static ManifestFinding? FindFinding(List<ManifestFinding> findings, FindingCategory[] categories, string keyword) =>
        findings.FirstOrDefault(f =>
            categories.Contains(f.Category) &&
            f.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static void AddChildProperty(List<ManifestProperty> props, XElement parent, XName name, string label, string explanation)
    {
        var el = parent.Element(name);
        if (el is not null)
            props.Add(new ManifestProperty { Label = label, Value = el.Value, Explanation = explanation });
    }

    private static void AddAttrProperty(List<ManifestProperty> props, XElement el, string attr, string label, string explanation)
    {
        var a = el.Attribute(attr);
        if (a is not null)
            props.Add(new ManifestProperty { Label = label, Value = a.Value, Explanation = explanation });
    }

    private static string ExplainDeviceFamily(string? name, string? minVersion) =>
        $"{name switch
        {
            "Windows.Desktop" => "Runs on Windows desktop PCs and tablets",
            "Windows.Universal" => "Runs on all Windows 10/11 device types",
            "Windows.Mobile" => "Originally targeted Windows Phone (deprecated)",
            "Windows.Xbox" => "Runs on Xbox consoles",
            "Windows.Holographic" => "Runs on HoloLens mixed reality devices",
            _ => $"Targets {name}"
        }}. {ExplainWindowsVersion(minVersion)}";

    private static string ExplainWindowsVersion(string? version) => version switch
    {
        "10.0.17763.0" => "Requires Windows 10 1809 (Oct 2018) or later.",
        "10.0.18362.0" => "Requires Windows 10 1903 (May 2019) or later.",
        "10.0.19041.0" => "Requires Windows 10 2004 (May 2020) or later.",
        "10.0.19045.0" => "Requires Windows 10 22H2 or later.",
        "10.0.22000.0" => "Requires Windows 11 (original) or later.",
        "10.0.22621.0" => "Requires Windows 11 22H2 or later.",
        "10.0.26100.0" => "Requires Windows 11 24H2 or later.",
        null => "",
        _ => $"Requires build {version} or later."
    };

    private static string ExplainPackageDependency(string? name) => name switch
    {
        "Microsoft.VCLibs.140.00" => "Visual C++ 2015-2022 Runtime. Required by C++ apps.",
        "Microsoft.VCLibs.140.00.UWPDesktop" => "Visual C++ Desktop Bridge Runtime. Required by Win32 apps packaged as MSIX.",
        "Microsoft.VCLibs.110.00" => "Visual C++ 2012 Runtime (legacy).",
        "Microsoft.VCLibs.120.00" => "Visual C++ 2013 Runtime (legacy).",
        var n when n?.Contains("WebView2", StringComparison.OrdinalIgnoreCase) == true =>
            "Edge WebView2 Runtime. App uses an embedded Chromium browser.",
        var n when n?.Contains(".NET", StringComparison.OrdinalIgnoreCase) == true =>
            ".NET Runtime. App is built on the .NET platform.",
        _ => $"Framework package: {name}. Must be installed for the app to run."
    };

    private static string ExplainLanguageCode(string code) => code.ToLowerInvariant() switch
    {
        "en-us" => "English (United States)", "en-gb" => "English (United Kingdom)",
        "fr-fr" => "French (France)", "de-de" => "German (Germany)",
        "es-es" => "Spanish (Spain)", "it-it" => "Italian (Italy)",
        "ja-jp" => "Japanese (Japan)", "ko-kr" => "Korean (South Korea)",
        "zh-cn" => "Chinese (Simplified)", "zh-tw" => "Chinese (Traditional)",
        "pt-br" => "Portuguese (Brazil)", "ru-ru" => "Russian (Russia)",
        "ar-sa" => "Arabic (Saudi Arabia)", "nl-nl" => "Dutch (Netherlands)",
        "sv-se" => "Swedish (Sweden)", "nb-no" => "Norwegian Bokmål",
        "da-dk" => "Danish (Denmark)", "fi-fi" => "Finnish (Finland)",
        "pl-pl" => "Polish (Poland)", "tr-tr" => "Turkish (Turkey)",
        _ => code
    };

    private static string ExplainRestrictedCapability(string name) => name switch
    {
        "runFullTrust" => "Runs with full-trust permissions — equivalent to a traditional desktop app. NOT sandboxed.",
        "broadFileSystemAccess" => "Accesses the entire user filesystem (Documents, Desktop, Downloads) without file picker prompts.",
        "appDiagnostics" => "Reads diagnostic data about other running apps (process names, resource usage).",
        "packageManagement" => "Installs, removes, and manages other application packages.",
        "appCaptureSettings" => "Accesses screen capture settings and can initiate recordings.",
        "appointmentsSystem" => "Reads, creates, and modifies system calendar appointments.",
        "contactsSystem" => "Reads and modifies the system contacts database.",
        "documentsLibrary" => "Accesses the Documents library without file picker prompts.",
        "picturesLibrary" => "Accesses the Pictures library directly.",
        "videosLibrary" => "Accesses the Videos library directly.",
        "musicLibrary" => "Accesses the Music library directly.",
        "removableStorage" => "Accesses files on USB drives and SD cards directly.",
        "enterpriseDataPolicy" => "Participates in Windows Information Protection (WIP) enterprise data policies.",
        "inputInjectionBrokered" => "Injects keystrokes and mouse clicks into other applications.",
        "unvirtualizedResources" => "Bypasses MSIX container isolation for filesystem and registry.",
        "smsSend" => "Sends SMS messages from the device.",
        _ => $"Restricted capability '{name}'. Requires Microsoft Store approval. Research what this grants."
    };

    private static string ExplainStandardCapability(string name) => name switch
    {
        "internetClient" => "Makes outbound HTTP/HTTPS and WebSocket connections.",
        "internetClientServer" => "Makes outbound connections AND accepts inbound connections as a server.",
        "privateNetworkClientServer" => "Communicates with devices on the local network (LAN), both as client and server.",
        "allJoyn" => "Uses AllJoyn for IoT device-to-device communication.",
        "codeGeneration" => "Generates code at runtime using JIT compilation.",
        _ => $"Standard capability '{name}'. Available to all packaged apps."
    };

    private static string ExplainDeviceCapability(string name) => name switch
    {
        "microphone" => "Accesses the microphone for audio capture. Requires user consent.",
        "webcam" => "Accesses the camera for photo/video. Requires user consent.",
        "location" => "Accesses geographic location (GPS, Wi-Fi, IP-based).",
        "proximity" => "Uses NFC for short-range data exchange.",
        "bluetooth" => "Discovers and communicates with Bluetooth devices.",
        "serialCommunication" => "Accesses serial (COM) ports for hardware communication.",
        "usb" => "Accesses USB devices beyond standard keyboards and mice.",
        "humanInterfaceDevice" => "Accesses HID-class USB devices (joysticks, controllers).",
        "pointOfService" => "Accesses point-of-service devices (barcode scanners, printers).",
        "lowLevelDevices" => "Accesses low-level hardware buses (I2C, SPI, GPIO).",
        "gazeInput" => "Accesses eye-tracking hardware.",
        _ => $"Device capability '{name}'. Grants hardware access."
    };

    private static string ExplainBackgroundTaskType(string type) => type switch
    {
        "pushNotification" => "Activates on push notification arrival. Processes notifications even when app is closed.",
        "timer" => "Activates on periodic schedule. Used for data sync and polling.",
        "systemEvent" => "Activates on system events (network change, user login, timezone change).",
        "audio" => "Keeps running for background audio playback or recording.",
        "controlChannel" => "Maintains persistent network connections (chat, VoIP) in background.",
        "location" => "Activates on geofence or location changes. Tracks location in background.",
        "general" => "General-purpose background task. Trigger depends on registration code.",
        _ => $"Background task type: {type}."
    };
}
