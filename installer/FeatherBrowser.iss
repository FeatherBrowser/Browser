#ifndef AppVersion
  #error AppVersion must be supplied by scripts/installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by scripts/installer.ps1
#endif
#ifndef OutputDirPath
  #error OutputDirPath must be supplied by scripts/installer.ps1
#endif
#ifndef BootstrapperPath
  #error BootstrapperPath must be supplied by scripts/installer.ps1
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
Source: "{#BootstrapperPath}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\Feather Browser"; Filename: "{app}\FeatherBrowser.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Feather Browser"; Filename: "{app}\FeatherBrowser.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\FeatherBrowser.exe"; Description: "Launch Feather Browser"; Flags: nowait postinstall skipifsilent

[Code]
function HasRuntimeAt(Root: Integer): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(Root,
    'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

function HasWebView2: Boolean;
begin
  Result := HasRuntimeAt(HKLM32) or HasRuntimeAt(HKCU);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if HasWebView2 then
    Exit;
  WizardForm.StatusLabel.Caption := 'Installing Microsoft Edge WebView2 Runtime (internet required)...';
  ExtractTemporaryFile('MicrosoftEdgeWebView2Setup.exe');
  if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebView2Setup.exe'),
    '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'Could not start Microsoft Edge WebView2 setup. Please retry setup.';
    Exit;
  end;
  if not HasWebView2 then
    Result := 'Microsoft Edge WebView2 Runtime could not be installed (code ' +
      IntToStr(ResultCode) + '). Check your internet connection and retry setup.';
end;
