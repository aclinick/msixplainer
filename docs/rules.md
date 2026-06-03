# Rules Reference

MSIXplainer's rules engine emits **findings** with a stable `RuleId`, a default severity, and explanation text. You can override the severity of any rule without changing the rule text by dropping a JSON file at:

```
%LOCALAPPDATA%\MSIXplainer\rules.json
```

The CLI also accepts `--rules <file>` to layer an additional override file on top — useful for checking team-wide rules into a CI repo.

## Override file format

```json
{
  "trust.fullTrust": "Info",
  "services.windowsService": "Warning",
  "capability.broadFileSystemAccess": "Critical",
  "capability.*": "Review"
}
```

- **Valid severities:** `Info`, `Review`, `Warning`, `Critical` (case-insensitive)
- **Unknown rule IDs** are skipped with a warning
- **Unrecognized severities** are skipped with a warning
- **Wildcards** (`capability.*`, `network.*`, `device.*`) override any rule in that family not explicitly listed
- Rule **text** (Title, Description, WhyItMatters, Recommendation) is compiled in and is **not** user-editable — only severity is

## Live catalog

To see the catalog with current overrides applied, run:

```powershell
msixplainer rules list
```

The table below is the source-of-truth catalog as of writing. Defaults can shift between releases; always cross-check against `rules list` for the version you're running.

### Identity

| Rule ID | Default | Detects |
|---|---|---|
| `identity.package` | Info | Package identity (name, publisher, version) |
| `identity.simplePublisher` | Review | Publisher certificate looks self-signed or simple |

### Trust

| Rule ID | Default | Detects |
|---|---|---|
| `trust.fullTrust` | Info | Package declares `runFullTrust` (normal for desktop-bridge apps) |
| `trust.appContainer` | Info | Runs inside the AppContainer sandbox (positive signal) |
| `trust.allowElevation` | Warning | Package can request UAC elevation to administrator |

### Restricted capabilities

| Rule ID | Default | Detects |
|---|---|---|
| `capability.broadFileSystemAccess` | Warning | Read/write access to the entire user filesystem |
| `capability.appCaptureSettings` | Info | Access to screen-capture settings |
| `capability.packageManagement` | Critical | Can install, update, or remove other MSIX packages |
| `capability.appDiagnostics` | Info | Can read diagnostic info about other running apps |
| `capability.appointmentsSystem` | Info | Access to system calendar appointments |
| `capability.contactsSystem` | Info | Access to system contacts database |
| `capability.documentsLibrary` | Info | Direct access to the user's Documents library |
| `capability.picturesLibrary` | Review | Direct access to the user's Pictures library |
| `capability.videosLibrary` | Review | Direct access to the user's Videos library |
| `capability.musicLibrary` | Review | Direct access to the user's Music library |
| `capability.removableStorage` | Info | Direct access to removable storage devices |
| `capability.enterpriseDataPolicy` | Review | Can participate in enterprise data protection (WIP) |
| `capability.inputInjectionBrokered` | Critical | Can inject synthetic keyboard/mouse input into other apps |
| `capability.userDataTasks` | Review | Access to the user's to-do tasks |
| `capability.smsSend` | Info | Can send SMS messages |
| `capability.unvirtualizedResources` | Warning | Direct (unvirtualized) filesystem/registry access |
| `capability.unknownRestricted` | Review | A restricted capability not in the catalog |
| `capability.*` | Review | Wildcard for any `capability.<name>` not above |

### Network / standard capabilities

| Rule ID | Default | Detects |
|---|---|---|
| `network.internetClient` | Info | Outbound internet client access |
| `network.internetClientServer` | Review | Outbound internet AND inbound server connections |
| `network.privateNetworkClientServer` | Review | Access to devices on the local/home network |
| `network.vpnPlugin` | Critical | Registers as a VPN plug-in (intercepts and routes traffic) |
| `network.*` | Review | Wildcard for any `network.<name>` not above |

### Device capabilities

| Rule ID | Default | Detects |
|---|---|---|
| `device.microphone` | Info | Microphone access |
| `device.webcam` | Info | Camera access |
| `device.location` | Review | Geographic location access |
| `device.proximity` | Review | NFC / proximity access |
| `device.bluetooth` | Review | Bluetooth device access |
| `device.serialCommunication` | Info | Serial (COM) port access |
| `device.usb` | Info | USB device access beyond standard HID |
| `device.humanInterfaceDevice` | Review | HID-class USB device access |
| `device.pointOfService` | Review | Point-of-service hardware access |
| `device.lowLevelDevices` | Info | Low-level hardware buses (I2C, SPI, GPIO) |
| `device.gazeInput` | Review | Eye-tracking hardware access |
| `device.unknown` | Review | A device capability not in the catalog |
| `device.*` | Review | Wildcard for any `device.<name>` not above |

### Startup

| Rule ID | Default | Detects |
|---|---|---|
| `startup.task` | Info | App registers a startup task that runs at login |

### Protocols / file associations

| Rule ID | Default | Detects |
|---|---|---|
| `protocols.handler` | Review | URI protocol handler (e.g. `myapp://...`) |
| `protocols.appUri` | Review | `https://` URI handler for specific hosts |
| `fileAssoc.handler` | Info | File-type association (e.g. `.docx`) |

### Virtualization

| Rule ID | Default | Detects |
|---|---|---|
| `virt.filesystemDisabled` | Critical | App actively disables MSIX filesystem write virtualization |
| `virt.registryDisabled` | Critical | App actively disables MSIX registry write virtualization |

### COM

| Rule ID | Default | Detects |
|---|---|---|
| `com.outProcServer` | Review | Out-of-process COM servers |
| `com.inProcServer` | Info | In-process COM servers (shell extensions, etc.) |

### Background / integrations

| Rule ID | Default | Detects |
|---|---|---|
| `background.task` | Review | App registers a background task |
| `office.integration` | Review | Office or Outlook integration indicators |
| `office.extension` | Review | App registers an Office-specific extension |
| `webview2.dependency` | Info | Depends on the Microsoft Edge WebView2 Runtime |

### VDI / deployment

| Rule ID | Default | Detects |
|---|---|---|
| `vdi.perUserInstall` | Info | Per-user installation is supported |
| `vdi.externalContent` | Review | Package allows external content to be loaded |

### Services

| Rule ID | Default | Detects |
|---|---|---|
| `services.windowsService` | Critical | Installs a Windows service that runs as a background system process |

---

## When to tune severities

Use overrides to align the engine with **your org's policy**, not the package author's intent. Examples:

| Scenario | Override |
|---|---|
| Internal LOB apps always get `runFullTrust` | `"trust.fullTrust": "Info"` (already the default) |
| You ship a managed VPN solution and trust its publishers | `"network.vpnPlugin": "Review"` |
| Your environment forbids any kernel/service installation | `"services.windowsService": "Critical"` (default) |
| You want loud alerts on any unknown capability | `"capability.unknownRestricted": "Warning"` |

The CI workflow gates on the **effective** severity after overrides, so a checked-in `rules.json` becomes your team's policy as code.
