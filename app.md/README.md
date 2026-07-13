# Chat Context — Screen Off Bug

## Project

`C:\projects\screen-off` — A single-file C# Windows Forms app (`ScreenOff.cs`) compiled
to a small standalone `screen-off.exe` via `csc.exe` (no NuGet dependencies). It lives
in the system tray and registers two global hotkeys:

- **Default Screen Off:** `Alt+D` — turns monitors off
- **Default Screensaver:** `Alt+S` — plays a video screensaver (videos in the
  bundled `videos/` folder) or falls back to the system screensaver if none are
  found

It also supports configurable hotkeys, an activation delay for screen off, an idle
auto-play timer, playback modes (Shuffle / Sequential / Single Loop), a mute
toggle, and Run-at-Windows-Startup registration.

## The User's Report

> "My screen turns on after a few seconds even though I did nothing."

The user was not seeing input — physical mouse/keyboard activity — yet the panel
was waking back up shortly after a screen-off action.

## My First Diagnosis (WRONG)

I claimed `lParam = 2` in the `SC_MONITORPOWER` call was the `MONITOR_ON` value
(incorrectly flipping it to `MONITOR_ON`) and changed both call sites to use
`lParam = 1`. The user correctly pushed back: **the screen was still waking up**.

## The Real Root Cause

Re-analyzed the `SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, lParam)`
call at:

- `ScreenOff.cs:977` — `TriggerScreenOff` (immediate path)
- `ScreenOff.cs:1114` — `CountdownTimer_Tick` (countdown completion path)

The actual bug: **`SendMessage` to `HWND_BROADCAST` does NOT broadcast** — it only
delivers to a single top-level window. So the `SC_MONITORPOWER` off command was
landing on a random window that ignored it, the monitor never actually received
an off command, and a few seconds later Windows' default power policy turned the
panel back on.

Also, `lParam = 2` was always correct: per `Winuser.h`, `2` = `MONITOR_OFF` and
`1` = `MONITOR_ON`. My first "fix" had inverted that, which made the wake-up
behaviour worse.

## The Fix

1. **Switch `SendMessage` → `PostMessage`.** `PostMessage` to `HWND_BROADCAST`
   actually broadcasts, so the shell receives the off command and turns the
   monitor off.
2. **Keep `lParam = 2`** (`MONITOR_OFF`).
3. **Added the `PostMessage` P/Invoke** declaration next to the existing
   `SendMessage` ones.

### Files changed

- `ScreenOff.cs:65` — added `[DllImport("user32.dll")] PostMessage(...)`
- `ScreenOff.cs:978` — `TriggerScreenOff` immediate path: `PostMessage(... lParam:2)`
- `ScreenOff.cs:1116` — `CountdownTimer_Tick` completion path: same change

### Build

```powershell
cd C:\projects\screen-off
powershell -ExecutionPolicy Bypass -File build.ps1
```

I could not run the build myself — the harness denied the
`-ExecutionPolicy Bypass` PowerShell call. The user has to run it.

## Other Code Observations

- **Idle timer (`IdleTimer_Tick`, `ScreenOff.cs:1086`)** runs every second. If
  the user has videos in `videos/` and `_runIdleCheck` is enabled (default
  `true` depending on saved config), the screensaver launches after the
  configured idle period (default 15 min). That can *also* appear as the
  "screen waking up" — but only on the configured idle interval, not after a
  few seconds. If the wake-up recurs at a regular interval matching the idle
  timeout, that's the screensaver, not a bug.
- The screensaver `VideoWindow` (`ScreenOff.cs:1217`) is a WPF window with
  `WindowState=Maximized`, `Topmost=true`, `Cursor=None`. It has its own
  `MediaEnded` re-play loop and change-video hotkeys (Space / Right / N / Enter).
- `MediaElement.MediaEnded` restart (`_mediaElement.Position = TimeSpan.Zero;
  _mediaElement.Play();`) at `ScreenOff.cs:1248` will loop the same video
  forever in Single Loop mode and forever-increment the index in Sequential
  mode — there is no exit if no one closes the window.

## Lessons

- Always double-check `Winuser.h` `MONITOR_*` constant values: `1` = ON,
  `2` = OFF, `0` = STANDBY. I had them reversed in my first attempt.
- `SendMessage(HWND_BROADCAST, ...)` is a common bug — `SendMessage` never
  broadcasts; `PostMessage` does. For `SC_MONITORPOWER` you need broadcast to
  reach the shell.
- The user's "do nothing" phrasing is the key signal — if it weren't for
  that, the most likely cause would have been input from a polling mouse or
  scheduled task. Since input is ruled out, the problem has to be in the
  app's own code or in Windows' default power policy interacting with a
  failed off command.

## Status

Code changes applied to `ScreenOff.cs`. Build not yet verified by me (harness
denied `powershell -ExecutionPolicy Bypass`). User needs to run `build.ps1`
and confirm the fix.
