; Build using Inno Setup 6.7 or newer. All paths are supplied by release.ps1.
[Setup]
AppId={{9B806C3B-61E5-4515-B175-2BC83EE6386D}
AppName=SnipLite
AppVersion={#AppVersion}
AppPublisher=GxDongV
AppPublisherURL=https://github.com/GxDongV/SnipLite
AppSupportURL=https://github.com/GxDongV/SnipLite/issues
AppUpdatesURL=https://github.com/GxDongV/SnipLite/releases
DefaultDirName={localappdata}\Programs\SnipLite
DefaultGroupName=SnipLite
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#ReleaseDir}
OutputBaseFilename=SnipLite-{#AppVersion}-win-x64-setup
LicenseFile={#ProjectRoot}\LICENSE
SetupIconFile={#ProjectRoot}\logo\SnipLite.ico
UninstallDisplayIcon={app}\SnipLite.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Global\SnipLite.SingleInstance
SetupMutex=SnipLite.Setup
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SnipLite"; Filename: "{app}\SnipLite.exe"
Name: "{group}\Uninstall SnipLite"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SnipLite"; Filename: "{app}\SnipLite.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SnipLite.exe"; Description: "Launch SnipLite"; Flags: nowait postinstall skipifsilent unchecked

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunCommand: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SnipLite', RunCommand) then
      if CompareText(RunCommand, '"' + ExpandConstant('{app}\SnipLite.exe') + '"') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SnipLite');
end;
