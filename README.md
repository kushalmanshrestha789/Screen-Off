# Screen Off & Screensaver Utility

A lightweight, system tray-based Windows utility that allows you to instantly turn off your screens or trigger your system screensaver via global hotkeys (default: `Alt+D` for screen off, `Alt+S` for screensaver) or the system tray context menu.

## Features

- **Double Hotkey Triggers:**
  - **Screen Off:** Turns off your monitors instantly.
  - **Screensaver:** Plays your system-wide active screensaver.
- **Customizable Keybinds:** Select custom modifier keys (`Alt`, `Ctrl`, `Shift`, `Win`, or `None`) and trigger keys (`A-Z`, `F1-F12`) for both actions independently.
- **Countdown Delay:** Optional delay timer (0s, 1s, 2s, 3s, 5s) for the screen off feature, giving you time to release your mouse and keyboard so they do not immediately wake the monitor back up.
- **Run at Windows Startup:** Easily toggle startup registration within the settings UI.
- **Beautiful Dark Theme UI:** Redesigned borderless dashboard with modern flat controls.
- **Zero Dependencies:** Compiles into a tiny standalone Windows PE executable using the built-in .NET Framework compiler (`csc.exe`).
- **Activity Logs:** View live logging for hotkey registrations and actions, complete with "Clear Log" and "Open Config Folder" actions.
- **Desktop Shortcut Creator:** Create a direct desktop shortcut with one click.

## Installation

1. Open PowerShell in the project directory and run the installer:
   ```powershell
   powershell -ExecutionPolicy Bypass -File install.ps1
   ```
2. The utility will compile, copy itself to `%USERPROFILE%\.local\bin`, start running in your system tray, and add the installation folder to your User `PATH`.

## How to Use

- **Turn Off Screen:** Press `Alt+D` (default) or click **TURN OFF SCREEN NOW** in the dashboard.
- **Start Screensaver:** Press `Alt+S` (default) or click **PLAY SCREENSAVER NOW** in the dashboard.
- **Open Settings Dashboard:** Double-click the system tray icon, or right-click the icon and choose **Open Dashboard**.
- **Exit Utility:** Right-click the system tray icon and choose **Exit**.

## Manual Build

If you only want to compile the executable without copying or installing it:
```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```
This will produce a `screen-off.exe` file in the project folder.
