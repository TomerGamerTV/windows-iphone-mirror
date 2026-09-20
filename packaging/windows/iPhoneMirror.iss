#define MyAppName "iPhone Mirror"
#define MyAppVersion "0.2.0-alpha"
#define MyAppPublisher "TomerGamerTV"
#define MyAppExeName "iPhoneMirror.exe"

[Setup]
AppId={{4E5B1E3D-3F2A-4F8E-9B9D-18F1D98E167B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\iPhoneMirror
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\artifacts\installer
OutputBaseFilename=iPhoneMirror-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion=0.2.0.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion=0.2.0.0
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function AppleDeviceEndpointAvailable(): Boolean;
var
  ResultCode: Integer;
  PowerShell: String;
  Params: String;
begin
  PowerShell := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ' +
    '"try{$c=New-Object Net.Sockets.TcpClient;$a=$c.BeginConnect(''127.0.0.1'',27015,$null,$null);' +
    'if(-not $a.AsyncWaitHandle.WaitOne(750)){exit 2};$c.EndConnect($a);$c.Close();exit 0}catch{exit 2}"';
  Result := Exec(PowerShell, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not AppleDeviceEndpointAvailable()) then
    MsgBox('iPhone Mirror needs Apple Devices for Windows so it can communicate with an iPhone over USB. Install Apple Devices from Microsoft Store, then reconnect the phone.', mbInformation, MB_OK);
end;
