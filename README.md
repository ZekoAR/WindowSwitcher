# WindowSwitcher

Hold **Ctrl+Shift** and press the **right mouse button**: a popup shows a thumbnail of every open window
on the monitor the mouse is on. Move the mouse over a thumbnail and release the button to switch to that
window.

- **One row per program.** A program with several windows shows them side by side in its row. Rows are
  in Alt-Tab order: the program you used last is on top.
- **The popup opens just right of the pointer.** If it does not fit on the monitor, it is moved until it
  fits, and the pointer jumps to the vertical middle of the rows, just left of the thumbnails.
- **Releasing over a thumbnail** brings that window to the front (restoring it if it is minimized) and
  moves the pointer to the window's center. Releasing anywhere else just closes the popup.
- **The right-click never reaches the window under the pointer**, so no context menu opens while you use
  the gesture. Moving the mouse works as normal.
- **Each window is captured once, when the popup opens.** Tiles show the program's icon until their
  picture arrives (typically within about 50 ms). Nothing is captured while the popup is closed.
- **Minimized windows** show the last image Windows kept of them (the same picture Alt-Tab and taskbar
  previews show), because Windows does not draw a minimized window.
- If there are many windows, all thumbnails shrink by the same amount so the popup still fits on the
  monitor.

## Running

`WindowSwitcher.exe` is a single file with nothing to install. It runs in the background with an icon in
the notification area (it may be inside the "hidden icons" overflow):

- **Left-click** the icon to open **Settings**.
- **Right-click** it for **Settings...** and **Exit**.

Only one copy runs at a time. To start it with Windows, put a shortcut to the exe in the folder that
`Win+R` -> `shell:startup` opens.

## Settings

The settings window has two things to set:

- **Which keys to hold** while pressing the right mouse button: any combination of Ctrl, Shift, Alt and
  Win, at least one. The keys held must match exactly, so with Ctrl+Shift chosen, Ctrl+Shift+Alt does
  not open the popup.
- **Thumbnail height** in pixels at 100% display scaling (60 to 400, default 140). On a monitor with
  higher scaling the thumbnails are scaled with it.

Changes apply at once. They are saved to `WindowSwitcher.json` next to the exe, so the folder must be
writable for settings to be kept:

```json
{
  "Hotkey": { "Ctrl": true, "Shift": true, "Alt": false, "Win": false },
  "ThumbnailHeight": 140
}
```

## Known limits

- Windows on other virtual desktops are not shown, and neither are windows smaller than 32 pixels.
- While a window running as administrator is active, Windows may not pass mouse input to a
  non-administrator WindowSwitcher, so the gesture may not work over it. This has not been tested.
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

This publishes a Native AOT single exe to `dist\WindowSwitcher.exe`. Exit a running copy first; the build
stops with a message if the exe is in use.

Window capture uses Windows Graphics Capture through the CsWinRT projection that the
`net10.0-windows10.0.22621.0` target framework brings in. The UI is plain Win32, because WinForms and WPF
do not support Native AOT.

## Diagnostics and gates

- `WindowSwitcher.exe --dump <folder>` lists the windows on the monitor under the pointer, grouped as
  the popup groups them, with the capture time for each. It writes `dump.txt` and one PNG per thumbnail.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools\gate-popup.ps1` is the end-to-end gate. It
  **uses the real mouse and keyboard for about 20 seconds** on the primary monitor:
  - it opens two test windows of its own, starts `dist\WindowSwitcher.exe`, and performs the gesture;
  - it checks switching, positioning, the settings window and the tray menu;
  - it writes screenshots and `result.txt` to `.gate\B`, then closes everything it started.
- With the environment variable `WINDOWSWITCHER_GATE_LAYOUT=<file>` set, the app writes the popup's and
  tiles' screen positions to that file each time the popup opens. The gate uses this.
- `tools\make-icon.ps1` redraws `src\WindowSwitcher\WindowSwitcher.ico`.
