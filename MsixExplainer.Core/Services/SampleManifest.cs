namespace MsixExplorer.Services;

/// <summary>
/// Provides a realistic Teams-like sample manifest for testing.
/// This manifest exercises most rules in the engine.
/// </summary>
public static class SampleManifest
{
    public static string GetTeamsLikeManifest() => """
        <?xml version="1.0" encoding="utf-8"?>
        <Package
          xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
          xmlns:mp="http://schemas.microsoft.com/appx/2014/phone/manifest"
          xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
          xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
          xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
          xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
          xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
          xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4"
          xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"
          xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
          IgnorableNamespaces="uap uap3 uap5 rescap desktop desktop4 desktop6 com">

          <Identity
            Name="Contoso.CollaborationHub"
            Publisher="CN=Contoso Ltd, O=Contoso Ltd, L=Redmond, S=Washington, C=US"
            Version="24.10.1.100"
            ProcessorArchitecture="x64" />

          <mp:PhoneIdentity PhoneProductId="A1B2C3D4-E5F6-7890-ABCD-EF1234567890"
                            PhonePublisherId="00000000-0000-0000-0000-000000000000"/>

          <Properties>
            <DisplayName>Contoso Collaboration Hub</DisplayName>
            <PublisherDisplayName>Contoso Ltd</PublisherDisplayName>
            <Logo>Assets\StoreLogo.png</Logo>
            <Description>Enterprise collaboration and communication platform with chat, meetings, and file sharing.</Description>
          </Properties>

          <Dependencies>
            <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
            <PackageDependency Name="Microsoft.VCLibs.140.00" MinVersion="14.0.30704.0" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" />
            <PackageDependency Name="Microsoft.VCLibs.140.00.UWPDesktop" MinVersion="14.0.30704.0" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" />
          </Dependencies>

          <Resources>
            <Resource Language="en-us"/>
            <Resource Language="fr-fr"/>
            <Resource Language="de-de"/>
            <Resource Language="ja-jp"/>
          </Resources>

          <Applications>
            <Application Id="App"
              Executable="ContosoHub.exe"
              EntryPoint="Windows.FullTrustApplication">

              <uap:VisualElements
                DisplayName="Contoso Collaboration Hub"
                Description="Chat, meet, call, and collaborate"
                BackgroundColor="transparent"
                Square150x150Logo="Assets\Square150x150Logo.png"
                Square44x44Logo="Assets\Square44x44Logo.png">
                <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
                <uap:SplashScreen Image="Assets\SplashScreen.png" />
              </uap:VisualElements>

              <Extensions>
                <!-- Startup task: launch at login -->
                <desktop:Extension Category="windows.startupTask" Executable="ContosoHub.exe" EntryPoint="Windows.FullTrustApplication">
                  <desktop:StartupTask TaskId="ContosoHubStartup" Enabled="true" DisplayName="Contoso Hub" />
                </desktop:Extension>

                <!-- Protocol handlers: custom URI schemes -->
                <uap:Extension Category="windows.protocol">
                  <uap:Protocol Name="contoso-hub">
                    <uap:DisplayName>Contoso Hub Link</uap:DisplayName>
                  </uap:Protocol>
                </uap:Extension>

                <uap:Extension Category="windows.protocol">
                  <uap:Protocol Name="contoso-meeting">
                    <uap:DisplayName>Contoso Meeting Link</uap:DisplayName>
                  </uap:Protocol>
                </uap:Extension>

                <uap:Extension Category="windows.protocol">
                  <uap:Protocol Name="contoso-call">
                    <uap:DisplayName>Contoso Call Link</uap:DisplayName>
                  </uap:Protocol>
                </uap:Extension>

                <!-- App URI handler: intercept web links -->
                <uap3:Extension Category="windows.appUriHandler">
                  <uap3:AppUriHandler>
                    <uap3:Host Name="contoso.com" />
                    <uap3:Host Name="hub.contoso.com" />
                    <uap3:Host Name="meet.contoso.com" />
                  </uap3:AppUriHandler>
                </uap3:Extension>

                <!-- File type associations -->
                <uap:Extension Category="windows.fileTypeAssociation">
                  <uap:FileTypeAssociation Name="contosohubfile">
                    <uap:SupportedFileTypes>
                      <uap:FileType>.chub</uap:FileType>
                      <uap:FileType>.contoso</uap:FileType>
                    </uap:SupportedFileTypes>
                  </uap:FileTypeAssociation>
                </uap:Extension>

                <!-- Background tasks -->
                <Extension Category="windows.backgroundTasks" EntryPoint="ContosoHub.BackgroundSync">
                  <BackgroundTasks>
                    <Task Type="pushNotification" />
                    <Task Type="timer" />
                    <Task Type="systemEvent" />
                  </BackgroundTasks>
                </Extension>

                <!-- COM server for Office integration -->
                <com:Extension Category="windows.comServer">
                  <com:ComServer>
                    <com:ExeServer Executable="ContosoHub.exe" DisplayName="Contoso Hub COM Server">
                      <com:Class Id="D1E2F3A4-B5C6-7890-1234-567890ABCDEF" DisplayName="Contoso.MeetingAddin" />
                      <com:Class Id="A1B2C3D4-E5F6-7890-ABCD-111111111111" DisplayName="Contoso.PresenceProvider" />
                    </com:ExeServer>
                  </com:ComServer>
                </com:Extension>
              </Extensions>
            </Application>
          </Applications>

          <!-- Filesystem and registry virtualization disabled -->
          <Properties>
            <desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>
            <desktop6:RegistryWriteVirtualization>disabled</desktop6:RegistryWriteVirtualization>
          </Properties>

          <Capabilities>
            <rescap:Capability Name="runFullTrust" />
            <Capability Name="internetClient" />
            <Capability Name="internetClientServer" />
            <Capability Name="privateNetworkClientServer" />
            <rescap:Capability Name="broadFileSystemAccess" />
            <rescap:Capability Name="appDiagnostics" />
            <DeviceCapability Name="microphone" />
            <DeviceCapability Name="webcam" />
          </Capabilities>
        </Package>
        """;
}
