# WindowSwitcher

Hold **Ctrl+Shift** and press the **right mouse button**: a popup shows a thumbnail of every open window
on the monitor the mouse is on. Move the mouse over a thumbnail and release the button to switch to that
window.

Hold **Ctrl+Shift+Alt** instead to see the windows of **all monitors** in one popup, on the monitor the
mouse is on. Everything below applies to both.

- **One row per program.** A program with several windows shows them side by side in its row, also
  when those windows are on different monitors.
- **No background.** The popup is only its tiles, each on its own dark plate with a light 1 px outline,
  2 px apart; whatever is behind the popup shows around them.
- **The order stays put.** Rows and windows keep the place they got the first time the popup showed
  them, unless you choose to order them by position (see Settings). Switching windows never
  reshuffles the popup. New programs and windows are added at the end. The order starts over when
  WindowSwitcher restarts; windows seen together for the first time are ordered front to back.
- **The popup opens with the active window's thumbnail centered under the pointer**, highlighted, so
  releasing at once keeps you where you are. If the active window is not in the popup (it is on another
  monitor, or the desktop is active), the foremost window's thumbnail on that monitor is used instead.
- **If there is not room for that**, the popup is moved just enough to fit on the monitor, and the
  pointer moves with the thumbnail so it is still under the pointer. Only when the monitor has no window
  to show does the popup open just right of the pointer instead.
- **Everything else is dimmed** (30% by default) while the popup is open, on every monitor (taskbar
  included); the thumbnails and the hovered window stay at full brightness. The dimming fades in and
  out quickly (120 ms), and clicks pass through it.
- **Hovering a thumbnail shows that window in front**: a live picture of it appears exactly where the
  window is, and disappears when the pointer leaves the thumbnail. Nothing
  about the real window changes (not its stacking order, focus, minimized state or Alt-Tab position);
  a minimized window appears where it would restore to.
- **Esc** closes the popup without switching, as if you released the button away from the thumbnails.
  The Esc key itself does not reach the window in front (so Ctrl+Shift+Esc does not open Task Manager
  here), and the later button release is swallowed too.
- **Releasing over a thumbnail** brings that window to the front (restoring it if it is minimized), gives
  it the keyboard focus, marks it with a flash (by default a border that flashes three times in
  300 ms), and moves the pointer to its center. Releasing anywhere else just closes the popup.
- **The right-click never reaches the window under the pointer**, so no context menu opens while you use
  the gesture. Moving the mouse works as normal.
- **Each window is captured once, when the popup opens.** Tiles show the program's icon until their
  picture arrives (typically within about 50 ms). Nothing is captured while the popup is closed.
- **Minimized windows** show the last image Windows kept of them (the same picture Alt-Tab and taskbar
  previews show), because Windows does not draw a minimized window.
- If there are many windows, all thumbnails shrink by the same amount so the popup still fits on the
  monitor.

## Installing and running

```
build.cmd --install
```

This builds the exe and then installs it for the current user (no administrator rights needed):

1. A running WindowSwitcher is asked to exit, the same way its tray menu's Exit does.
2. The exe is copied to `%LOCALAPPDATA%\Programs\WindowSwitcher\`. A `WindowSwitcher.json` already there
   is kept.
3. It is set to start with Windows (the `WindowSwitcher` value under
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`). If you have turned WindowSwitcher off in
   **Task Manager > Startup apps**, it stays off and the install says so.
4. The installed copy is started.

Run it again after changing the code to update the installed copy. To stop WindowSwitcher from starting
with Windows, turn it off in **Task Manager > Startup apps**. There is no uninstall option: to remove it,
exit it, delete the folder, and delete the `WindowSwitcher` value under the `Run` key above.

WindowSwitcher is a single file, so `dist\WindowSwitcher.exe` also runs fine without installing. It runs
in the background with an icon in the notification area (it may be inside the "hidden icons" overflow):

- **Left-click** the icon to open **Settings**.
- **Right-click** it for **Settings...** and **Exit**.

Only one copy runs at a time.

## Settings

The settings window has these things to set:

- **Which keys to hold** while pressing the right mouse button, in two rows: **This monitor** (default
  Ctrl+Shift) and **All monitors** (default Ctrl+Shift+Alt). Each row is any combination of Ctrl,
  Shift, Alt and Win, with at least one key, and the two rows must differ. The keys held must match a
  row exactly: with the defaults, Ctrl+Shift+Alt shows all monitors, and a right-click with Ctrl+Alt is
  just a normal right-click.
- **Thumbnail height** in pixels at 100% display scaling (60 to 400, default 105). On a monitor with
  higher scaling the thumbnails are scaled with it.
- **Dim everything else**: how much darker the rest of the screen gets while the popup is open, in
  percent (0 turns dimming off, at most 80, default 30).
- **Flash on switch**: how the window you switch to is marked, three times in 300 ms:
  - **None**: no flash.
  - **Border** (default): a border in your Windows accent color along the window's edge.
  - **Whole window**: the whole window lights up in the accent color.

  Both flashes let clicks through, so you can click in the window at once.
- **Row order**:
  - **Fixed** (default): rows keep the place they got when the popup first showed the program.
  - **By horizontal position**: rows go from the leftmost program on the screen to the rightmost, and
    the windows inside a row go left to right too. A window's place is its left edge (for a minimized
    window, where it restores to); a program's place is its leftmost window that is not minimized (or,
    if all are minimized, its leftmost restore place). Across monitors this follows the whole desktop
    from left to right.

Changes apply at once. They are saved to `WindowSwitcher.json` next to the exe, so the folder must be
writable for settings to be kept:

```json
{
  "Hotkey": { "Ctrl": true, "Shift": true, "Alt": false, "Win": false },
  "AllMonitorsHotkey": { "Ctrl": true, "Shift": true, "Alt": true, "Win": false },
  "ThumbnailHeight": 105,
  "DimPercent": 30,
  "SwitchFlash": "Border",
  "RowOrder": "Fixed"
}
```

`SwitchFlash` is `None`, `Border` or `Window`; `RowOrder` is `Fixed` or `Horizontal`. A setting missing
from the file gets its default.

## Known limits

- Windows on other virtual desktops are not shown, and neither are windows smaller than 32 pixels.
- WindowSwitcher runs without administrator rights. While a window running as administrator is
  active, Windows may not pass mouse input to it, so the gesture may not work there, and switching to
  such a window can fail. This has not been tested.
- On Windows 10, capturing may briefly show a yellow border around a window. Windows 11 lets
  WindowSwitcher turn that border off.
- Problems (a window that could not be captured or brought to the front) are written to
  `WindowSwitcher.log` next to the exe.

## Building

Requirements: the .NET 10 SDK and the Visual Studio C++ build tools ("Desktop development with C++"),
which Native AOT needs for linking.

```
build.cmd
```

This publishes a Native AOT single exe to `dist\WindowSwitcher.exe`. If a copy started from `dist\` is
running, exit it first; the build stops with a message if that exe is in use. `build.cmd --install` does
the same and then installs (see above); `build.cmd --help` lists the options.

To try the install step without touching your installed copy or the startup list, set
`WINDOWSWITCHER_INSTALL_DIR` to another folder and `WINDOWSWITCHER_INSTALL_NO_STARTUP=1`. Any running
WindowSwitcher is still asked to exit.

Window capture uses Windows Graphics Capture through the CsWinRT projection that the
`net10.0-windows10.0.22621.0` target framework brings in. The UI is plain Win32, because WinForms and WPF
do not support Native AOT.

## Diagnostics and gates

- `WindowSwitcher.exe --dump <folder>` lists the windows on the monitor under the pointer, grouped as
  the popup groups them, with the capture time for each. It writes `dump.txt` and one PNG per thumbnail.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools\gate-popup.ps1` is the end-to-end gate. It
  **uses the real mouse and keyboard for about 30 seconds**, mostly on the primary monitor:
  - a WindowSwitcher that is already running is asked to exit first, and is not restarted afterwards;
  - it opens test windows of its own (one on another monitor, if there is one), starts
    `dist\WindowSwitcher.exe`, and performs both gestures;
  - it checks switching, positioning, transparency, the fixed order, all monitors, the settings window
    and the tray menu;
  - it writes screenshots and `result.txt` to `.gate\B`, then closes everything it started.
- With the environment variable `WINDOWSWITCHER_GATE_LAYOUT=<file>` set, the app writes the popup's and
  tiles' screen positions to that file each time the popup opens. The gate uses this.
- `tools\make-icon.ps1` redraws `src\WindowSwitcher\WindowSwitcher.ico`.
