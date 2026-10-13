; Per-user Inno Setup installer (ADR 0010). Build:
;   ISCC /DVersion=X.Y.Z /DArch=x64|arm64 /DSource=<absolute path to ScreenFerry.exe> ScreenFerry.iss

#ifndef Version
  #error Pass /DVersion=X.Y.Z
#endif
#ifndef Source
  #error Pass /DSource=<absolute path to the published ScreenFerry.exe>
#endif
#ifndef Arch
  #define Arch "x64"
#endif
; Held by the running agent; see App.xaml.cs.
#define InstanceMutex "ScreenFerry.Instance"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{73C1E3E6-DE94-4E2F-AFB0-2E79F8A97912}
AppName=ScreenFerry
AppVersion={#Version}
AppVerName=ScreenFerry {#Version}
AppPublisher=ScreenFerry contributors
AppPublisherURL=https://github.com/ali-caglar/ScreenFerry
AppSupportURL=https://github.com/ali-caglar/ScreenFerry/issues
VersionInfoVersion={#Version}
DefaultDirName={autopf}\ScreenFerry
PrivilegesRequired=lowest
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.18362
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
CloseApplications=no
UninstallDisplayName=ScreenFerry
UninstallDisplayIcon={app}\ScreenFerry.exe
WizardStyle=modern
OutputBaseFilename=ScreenFerry-{#Version}-windows-{#Arch}-setup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

[Tasks]
Name: startup; Description: "Start ScreenFerry when I sign in to Windows"

[Files]
Source: "{#Source}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ScreenFerry"; Filename: "{app}\ScreenFerry.exe"

[Registry]
; Same value as the app's Start with Windows item. Silent updates leave the user's choice alone.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "ScreenFerry"; ValueData: """{app}\ScreenFerry.exe"""; Tasks: startup; Check: not WizardSilent

[Run]
Filename: "{app}\ScreenFerry.exe"; Parameters: "{code:LaunchParameters}"; Description: "Open ScreenFerry"; Flags: nowait postinstall

[Code]
procedure StopScreenFerry(WaitFirst: Boolean);
var
  I, ResultCode: Integer;
begin
  { An agent that started this setup to update itself quits on its own. }
  I := 0;
  while WaitFirst and CheckForMutexes('{#InstanceMutex}') and (I < 60) do
  begin
    Sleep(500);
    I := I + 1;
  end;
  if CheckForMutexes('{#InstanceMutex}') then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ScreenFerry.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    I := 0;
    while CheckForMutexes('{#InstanceMutex}') and (I < 20) do
    begin
      Sleep(250);
      I := I + 1;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopScreenFerry(WizardSilent);
  Result := '';
end;

function LaunchParameters(Param: String): String;
begin
  Result := ExpandConstant('{param:updated|}');
  if Result <> '' then
    Result := '--updated ' + Result;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopScreenFerry(False);
    RegDeleteValue(HKCU, '{#RunKey}', 'ScreenFerry');
  end;
end;
