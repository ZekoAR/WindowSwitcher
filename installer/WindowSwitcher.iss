; Per-user installer for WindowSwitcher (Inno Setup 6). Built by `build.cmd --setup`, which passes
; /DAppVersion=X.Y.Z and writes dist\WindowSwitcher-X.Y.Z-setup.exe from dist\WindowSwitcher.exe.
;
; It does what tools\install.ps1 does, without administrator rights:
;   - asks a running WindowSwitcher to exit, the way its tray menu's Exit does, and waits for it
;   - copies the exe to %LOCALAPPDATA%\Programs\WindowSwitcher; a WindowSwitcher.json there is kept
;   - starts it with Windows (HKCU Run value "WindowSwitcher"), unless that task is unchecked
;   - starts it
; and adds a Start menu entry and an uninstaller (Settings > Apps), which removes the folder,
; settings included, and the Run value.

#ifndef AppVersion
  #error Pass the version: ISCC /DAppVersion=X.Y.Z (build.cmd --setup does this)
#endif

#define Root AddBackslash(SourcePath) + ".."

[Setup]
AppId={{12AB4C6B-94D4-468F-BD1B-CDC88CF84D19}
AppName=WindowSwitcher
AppVersion={#AppVersion}
AppVerName=WindowSwitcher {#AppVersion}
AppPublisher=Goran Wiik Thomassen
AppPublisherURL=https://github.com/ZekoAR/WindowSwitcher
AppSupportURL=https://github.com/ZekoAR/WindowSwitcher/issues
AppCopyright=Copyright (c) 2026 Goran Wiik Thomassen
VersionInfoVersion={#AppVersion}
; Per user: no UAC prompt, and the folder stays writable for WindowSwitcher.json. The folder is fixed
; so the installer and `build.cmd --install` update the same copy.
PrivilegesRequired=lowest
DefaultDirName={userpf}\WindowSwitcher
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#Root}\dist
OutputBaseFilename=WindowSwitcher-{#AppVersion}-setup
SetupIconFile={#Root}\src\WindowSwitcher\WindowSwitcher.ico
UninstallDisplayIcon={app}\WindowSwitcher.exe
UninstallDisplayName=WindowSwitcher
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
; The [Code] below exits a running copy first; Restart Manager is the fallback. The installer starts
; WindowSwitcher itself, so Restart Manager must not start a second one.
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: startup; Description: "Start WindowSwitcher when I sign in to Windows"
Name: startmenu; Description: "Add WindowSwitcher to the Start menu"

[Files]
Source: "{#Root}\dist\WindowSwitcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\WindowSwitcher"; Filename: "{app}\WindowSwitcher.exe"; WorkingDir: "{app}"; Tasks: startmenu

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WindowSwitcher"; ValueData: """{app}\WindowSwitcher.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\WindowSwitcher.exe"; WorkingDir: "{app}"; Description: "Start WindowSwitcher now"; Flags: nowait postinstall

[UninstallDelete]
Type: files; Name: "{app}\WindowSwitcher.json"
Type: files; Name: "{app}\WindowSwitcher.log"
Type: dirifempty; Name: "{app}"

[Code]
const
  WM_CLOSE = $0010;
  SYNCHRONIZE = $00100000;
  WAIT_OBJECT_0 = 0;
  MainClassName = 'WindowSwitcher.Main';

function GetWindowThreadProcessId(Wnd: HWND; var ProcessId: Cardinal): Cardinal;
  external 'GetWindowThreadProcessId@user32.dll stdcall';
function OpenProcess(Access: Cardinal; Inherit: BOOL; ProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

{ Asks a running WindowSwitcher to exit and waits up to 5 seconds per round for its process to end,
  as tools\install.ps1 does. True when none is running any more. }
function ExitRunningCopy(): Boolean;
var
  Wnd: HWND;
  ProcessId: Cardinal;
  Process: THandle;
  Round: Integer;
begin
  Result := False;
  for Round := 0 to 4 do
  begin
    Wnd := FindWindowByClassName(MainClassName);
    if Wnd = 0 then
    begin
      Result := True;
      Exit;
    end;
    ProcessId := 0;
    GetWindowThreadProcessId(Wnd, ProcessId);
    Process := OpenProcess(SYNCHRONIZE, False, ProcessId);
    Log(Format('Asking the running WindowSwitcher (pid %d) to exit', [ProcessId]));
    PostMessage(Wnd, WM_CLOSE, 0, 0);
    if Process <> 0 then
    begin
      if WaitForSingleObject(Process, 5000) <> WAIT_OBJECT_0 then
        Log('It did not exit within 5 seconds');
      CloseHandle(Process);
    end
    else
      Sleep(1000);
  end;
  Result := FindWindowByClassName(MainClassName) = 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if ExitRunningCopy() then
    Result := ''
  else
    Result := 'WindowSwitcher is still running. Exit it from its tray icon and run Setup again.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := ExitRunningCopy();
  if not Result then
    MsgBox('WindowSwitcher is still running. Exit it from its tray icon and uninstall again.', mbError, MB_OK);
end;
