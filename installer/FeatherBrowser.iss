#ifndef AppVersion
  #error AppVersion must be supplied by scripts/installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by scripts/installer.ps1
#endif
#ifndef OutputDirPath
  #error OutputDirPath must be supplied by scripts/installer.ps1
#endif

[Setup]
AppId={{E499CDA6-AE38-45D6-A005-138B808BEF2A}
AppName=Feather Browser
AppVersion={#AppVersion}
AppPublisher=Feather Browser
DefaultDirName={localappdata}\Programs\FeatherBrowser
DefaultGroupName=Feather Browser
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDirPath}
OutputBaseFilename=FeatherBrowser-v{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\FeatherBrowser\Assets\Branding\feather.ico
UninstallDisplayIcon={app}\FeatherBrowser.exe
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Feather Browser"; Filename: "{app}\FeatherBrowser.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Feather Browser"; Filename: "{app}\FeatherBrowser.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\FeatherBrowser.exe"; Description: "Launch Feather Browser"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Classes\FeatherBrowserURL"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\FeatherBrowserHTML"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Clients\StartMenuInternet\FeatherBrowser"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\FeatherBrowser\Capabilities"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\FeatherBrowser"; Flags: dontcreatekey uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: none; ValueName: "Feather Browser"; Flags: dontcreatekey uninsdeletevalue

[InstallDelete]
Type: filesandordirs; Name: "{app}\locales"
Type: files; Name: "{app}\Microsoft.Web.WebView2.*.dll"
Type: files; Name: "{app}\WebView2Loader.dll"
