# WindowSwitcher

Switch windows with the mouse. Hold **Ctrl+Alt** and press the **right mouse button** to see a thumbnail
of every window on that monitor. Move onto one and let go to switch to it.

Hold **Ctrl+Shift+Alt** instead to see the windows on all your monitors.

## What it does

- **One row per program**, with that program's windows side by side. By default, rows and windows are
  ordered by where they are on the screen, from left to right.
- **Opens where you are.** The thumbnail of your current window opens under the pointer, so letting
  go straight away leaves you where you were.
- **Hover to preview.** The window you point at shows in its real place, and the rest of the screen
  dims. The real window stays as it was.
- **Let go to switch.** The window comes to the front (restored if it was minimized), gets the keyboard,
  and flashes briefly, and the pointer jumps to it. To cancel, let go away from the thumbnails or press
  **Esc**.
- **Stays out of the way.** The right-click never reaches the window under the pointer, and windows are
  captured only while the popup is open. Minimized windows show the last picture Windows kept of them.

## Install

Download `WindowSwitcher-X.Y.Z-setup.exe` from
[Releases](https://github.com/ZekoAR/WindowSwitcher/releases) and run it. It installs just for you, with
no admin rights needed. By default it starts with Windows and adds itself to the Start menu; you can
untick either one. Updating keeps your settings, and you uninstall it from **Settings > Apps**.

The installer isn't code-signed, so SmartScreen may warn about it the first time. If it does, choose
**More info > Run anyway**. If you'd rather not install, every release also includes the plain
`WindowSwitcher.exe`, which runs without installing.

WindowSwitcher lives in the notification area (look under the hidden-icons arrow). Click its icon for
Settings, or right-click it to exit.

## Settings

| Setting | Default | Options |
| --- | --- | --- |
| Keys for this monitor | Ctrl+Alt | Any mix of Ctrl, Shift, Alt and Win |
| Keys for all monitors | Ctrl+Shift+Alt | Same, but must differ from the keys above |
| Thumbnail height | 115 px | 60 to 400, scaled with the display |
| Dim everything else | 30% | 0 (off) to 80% |
| Flash on switch | Whole window | Whole window, border or none |
| Row order | By horizontal position | Or fixed, where rows keep the place they first got |

The keys must match exactly: with the defaults, Ctrl+Shift+right-click is still a normal right-click.
Changes apply at once and are saved to `WindowSwitcher.json` next to the exe.

## Known limits

- Windows on other virtual desktops aren't shown.
- WindowSwitcher runs without admin rights. While a window running as administrator is active, the
  gesture may not work, and switching to such a window may fail.
- On Windows 10, a yellow capture border may flash briefly around windows.
- Problems are logged to `WindowSwitcher.log` next to the exe.

## Building from source

You need the .NET 10 SDK and Visual Studio's C++ build tools ("Desktop development with C++").

- `build.cmd` builds `dist\WindowSwitcher.exe`, a single Native AOT exe.
- `build.cmd --install` also installs that exe for you, the same way the installer does, and starts it.

## License

MIT, see [LICENSE](LICENSE).
