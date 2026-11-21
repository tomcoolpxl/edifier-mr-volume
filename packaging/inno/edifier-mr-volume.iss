; Per-user installer for Edifier MR Volume: Inno Setup 6, no admin. Installs the
; self-contained app (what the release zip holds) to %LOCALAPPDATA%\Programs\EdifierMrVolume,
; the same folder tools\install.ps1 uses, so either one upgrades the other.
;
;   iscc /DAppVersion=1.1.0 /DArch=x64 /DSourceDir=..\..\artifacts\publish\win-x64 packaging\inno\edifier-mr-volume.iss
;   (/DArch=arm64 with ...\publish\win-arm64 for the ARM64 installer)
;
; Silent: edifier-mr-volume-vX.Y.Z-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
;         [/TASKS="autostart" | /TASKS=""] [/NOLAUNCH] [/LOG="file"]
; /NOLAUNCH skips starting the app after a silent install (used by CI).

#ifndef AppVersion
  #error Pass /DAppVersion=X.Y.Z
#endif
; x64 or arm64: which build the installer holds. The x64 one also installs on Arm PCs
; (Windows runs it through emulation); the arm64 one only there, natively.
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\win-" + Arch
#endif
; The release tag for the file name (v1.1.0, or v1.1.0-rc.1 for a pre-release);
; AppVersion stays the plain X.Y.Z that Windows' version fields need.
#ifndef FileTag
  #define FileTag "v" + AppVersion
#endif

[Setup]
AppId={{2C8E5984-B80D-4BC3-9745-2825F775DE75}
AppName=Edifier MR Volume
AppVersion={#AppVersion}
AppVerName=Edifier MR Volume {#AppVersion}
AppPublisher=tomcoolpxl
AppPublisherURL=https://github.com/tomcoolpxl/edifier-mr-volume
AppSupportURL=https://github.com/tomcoolpxl/edifier-mr-volume/issues
AppUpdatesURL=https://github.com/tomcoolpxl/edifier-mr-volume/releases
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Edifier MR Volume setup: the hardware volume of Edifier MR studio monitors in the notification area
DefaultDirName={localappdata}\Programs\EdifierMrVolume
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
; Windows 10 2004, as the app itself requires.
MinVersion=10.0.19041
LicenseFile={#SourceDir}\LICENSE
SetupIconFile=..\..\src\EdifierMrVolume\Assets\app.ico
UninstallDisplayIcon={app}\EdifierMrVolume.exe
UninstallDisplayName=Edifier MR Volume
OutputDir=..\..\artifacts\installer
OutputBaseFilename=edifier-mr-volume-{#FileTag}-win-{#Arch}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The running app is asked to exit cleanly in [Code] (it releases the speaker's Bluetooth
; link right away), so the Restart Manager has nothing left to close.
CloseApplications=no
RestartApplications=no
UsePreviousTasks=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start Edifier MR Volume when I sign in to Windows"

[InstallDelete]
; A clean copy every time, so files a newer version dropped don't linger.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Edifier MR Volume"; Filename: "{app}\EdifierMrVolume.exe"; WorkingDir: "{app}"; Comment: "Edifier MR studio monitor volume in the notification area"

[Registry]
; The same value the app's own "Start with Windows" menu item writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EdifierMrVolume"; ValueData: """{app}\EdifierMrVolume.exe"""; Tasks: autostart

[Run]
Filename: "{app}\EdifierMrVolume.exe"; Description: "Start Edifier MR Volume"; Flags: postinstall nowait skipifsilent
; After a silent upgrade, start it again unless /NOLAUNCH was given.
Filename: "{app}\EdifierMrVolume.exe"; Flags: nowait skipifnotsilent; Check: LaunchAfterSilentInstall

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function LaunchAfterSilentInstall: Boolean;
var
  I: Integer;
begin
  Result := True;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/NOLAUNCH') = 0 then
      Result := False;
end;

// True when a copy started from this install folder is running. "--exit" reaches any running
// copy, so without this check installing or uninstalling here would also close a copy that
// runs from somewhere else (Scoop, a portable zip). If Windows can't say, assume it is.
function RunningFromHere: Boolean;
var
  Locator, Service, Processes, Process: Variant;
  I: Integer;
  AppDir, Path: String;
begin
  Result := False;
  AppDir := Lowercase(AddBackslash(ExpandConstant('{app}')));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ExecutablePath FROM Win32_Process WHERE Name = ''EdifierMrVolume.exe''');
    for I := 0 to Processes.Count - 1 do
    begin
      Process := Processes.ItemIndex(I);
      if not VarIsNull(Process.ExecutablePath) then
      begin
        Path := Process.ExecutablePath;
        if Pos(AppDir, Lowercase(Path)) = 1 then
          Result := True;
      end;
    end;
  except
    Log('Could not list running processes; asking any running copy to exit.');
    Result := True;
  end;
end;

// Asks a running copy installed here to exit cleanly ("--exit" signals it and returns at
// once), then gives it a few seconds to let go of the speaker and its files.
procedure CloseRunningApp;
var
  Exe: String;
  ResultCode: Integer;
begin
  Exe := ExpandConstant('{app}\EdifierMrVolume.exe');
  if FileExists(Exe) and RunningFromHere then
  begin
    Log('Asking the running app to exit: ' + Exe + ' --exit');
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(3000);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningApp;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseRunningApp;
    // Remove "Start with Windows" only when it points at this copy.
    if RegQueryStringValue(HKCU, RunKey, 'EdifierMrVolume', Value) and
       (Pos(Lowercase(ExpandConstant('{app}\EdifierMrVolume.exe')), Lowercase(Value)) > 0) then
      RegDeleteValue(HKCU, RunKey, 'EdifierMrVolume');
  end;
end;
