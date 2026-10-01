; My Kaydence installer, built with Inno Setup 6 by build-installer.ps1
; I install just for me, in my own AppData, so it never needs an administrator

#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif
#define MyAppName "Kaydence"
#define MyAppPublisher "KaydeeCodes"
#define MyAppURL "https://kaydee.codes"
#define MyAppExeName "Kaydence.exe"

[Setup]
AppId={{F22CC1FE-B936-4C95-9CE4-60745E7ABE44}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL=https://github.com/KaydeeCodes/Kaydence/issues
AppUpdatesURL=https://github.com/KaydeeCodes/Kaydence/releases
DefaultDirName={localappdata}\Programs\Kaydence
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=Kaydence-Setup-{#MyAppVersion}
SetupIconFile=..\src\Kaydence\Assets\Kaydence.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; I close a running Kaydence before updating it, it saves itself as it closes
AppMutex=KaydeeCodes.Kaydence.Instance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; I only remove the start with Windows entry when uninstalling, Kaydence adds it itself when I switch it on
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Kaydence"; ValueType: none; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// I remind people that uninstalling never touches their diary
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox('Kaydence has been removed.' + #13#10#13#10 +
      'Your diary, settings and backups are still safe in ' + ExpandConstant('{localappdata}') + '\Kaydence.' + #13#10 +
      'If you install Kaydence again, everything will be right where you left it.', mbInformation, MB_OK);
end;
