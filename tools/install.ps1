# Installs WindowSwitcher for the current user and makes it start with Windows. Called by
# `build.cmd --install`. This changes the machine outside the repository:
#
#   1. asks a running WindowSwitcher to exit (the WM_CLOSE its tray menu's Exit also sends) and waits
#   2. copies the exe to %LOCALAPPDATA%\Programs\WindowSwitcher - per user, so no admin rights, and
#      writable, so WindowSwitcher.json next to the exe keeps working; an existing one is kept
#   3. sets HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WindowSwitcher to the installed exe
#   4. starts the installed copy
#
# WINDOWSWITCHER_INSTALL_DIR installs somewhere else; WINDOWSWITCHER_INSTALL_NO_STARTUP=1 skips step 3.
param(
    [Parameter(Mandatory = $true)][string]$Exe
)

$ErrorActionPreference = 'Stop'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$ApprovedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$ValueName = 'WindowSwitcher'

$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\WindowSwitcher'
if ($env:WINDOWSWITCHER_INSTALL_DIR) { $InstallDir = $env:WINDOWSWITCHER_INSTALL_DIR }
$Target = Join-Path $InstallDir 'WindowSwitcher.exe'

if (-not (Test-Path -LiteralPath $Exe)) { throw "Nothing to install: $Exe does not exist." }

Add-Type -Namespace WindowSwitcherInstall -Name Native -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string cls, string title);
[DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
public static IntPtr FindMainWindow() { return FindWindowW("WindowSwitcher.Main", null); }
'@

# 1. A running copy locks its exe and would also block the new one (only one copy runs at a time).
for ($round = 0; ; $round++) {
    $main = [WindowSwitcherInstall.Native]::FindMainWindow()
    if ($main -eq [IntPtr]::Zero) { break }
    if ($round -ge 5) { throw 'WindowSwitcher keeps running; exit it from its tray icon and install again.' }
    $procId = [uint32]0
    [WindowSwitcherInstall.Native]::GetWindowThreadProcessId($main, [ref]$procId) | Out-Null
    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
    Write-Output "  exiting the running WindowSwitcher (pid $procId, $($proc.Path))"
    [WindowSwitcherInstall.Native]::PostMessageW($main, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # WM_CLOSE
    if ($proc -and -not $proc.WaitForExit(5000)) {
        throw "WindowSwitcher (pid $procId) did not exit within 5 seconds; exit it from its tray icon and install again."
    }
}

# 2. Only the exe is copied; settings next to it survive.
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -LiteralPath $Exe -Destination $Target -Force
Write-Output "  installed $Target"

# 3. Start with Windows.
if ($env:WINDOWSWITCHER_INSTALL_NO_STARTUP -eq '1') {
    Write-Output '  start with Windows - skipped (WINDOWSWITCHER_INSTALL_NO_STARTUP=1)'
}
else {
    Set-ItemProperty -LiteralPath $RunKey -Name $ValueName -Value ('"{0}"' -f $Target)
    Write-Output "  starts with Windows: HKCU\...\CurrentVersion\Run\$ValueName"
    # Task Manager's Startup apps switch is kept here; an odd first byte means the user turned it off.
    $approved = (Get-ItemProperty -LiteralPath $ApprovedKey -Name $ValueName -ErrorAction SilentlyContinue).$ValueName
    if ($approved -and ($approved[0] -band 1)) {
        Write-Output '  NOTE: WindowSwitcher is turned off in Task Manager > Startup apps. Turn it on there to start it with Windows.'
    }
}

# 4. Run it now too.
Start-Process -FilePath $Target -WorkingDirectory $InstallDir
Write-Output '  started'
