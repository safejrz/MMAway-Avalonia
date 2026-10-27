# MMAway (Avalonia)

Cross-platform (Windows and Linux) port of [MMAway](https://github.com/safejrz/MMAway): a small tool that emulates mouse movement, clicks and typing so chat apps (e.g. MS Teams) don't flip to "Away", and keeps the machine from sleeping while it runs.

## Features
- Random mouse jitter, square / figure-eight patterns and a click on its own window.
- Random typing into its own text box.
- Keep-awake: `SetThreadExecutionState` on Windows, a `systemd-inhibit` idle/sleep lock on Linux.
- The window icon switches between stopped and running.
- Configurable global hotkey to start or stop activity.
- Optional auto-stop after a configured number of minutes (0 disables it).

## Platform support
| OS | Input | Keep-awake | Global hotkey |
|----|-------|-----------|--------------|
| Windows 10/11 | `user32` (`SetCursorPos`, `mouse_event`) | `SetThreadExecutionState` | `RegisterHotKey` |
| Linux, X11 session (Mint Cinnamon, etc.) | XTest (`libX11`, `libXtst`) | `systemd-inhibit` | X11 key grab |
| Linux, Wayland session | Only reaches XWayland apps (a warning is shown) | `systemd-inhibit` | Not supported |

The default global shortcut is `Ctrl+Alt+M`. It can be changed in Options using Ctrl, Alt, Shift, or Win with A-Z, 0-9, F1-F12, Space, Enter, Esc, or Tab. If the shortcut is already taken by another app, MMAway reports that it could not register it. Configure auto-stop in Options; `0` minutes disables it.

On Linux, install `libxtst6` if it is missing (`sudo apt install libxtst6`).

## Build and run
```bash
dotnet run --project src/MMAway
```

## Publish self-contained builds
```bash
dotnet publish src/MMAway -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64
dotnet publish src/MMAway -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o publish/win-x64
```
Pushing a `v*` tag builds both and attaches them to a GitHub release (see `.github/workflows/release.yml`).

## Layout
- `src/MMAway/ActivityEngine.cs`: platform-independent activity logic.
- `src/MMAway/Input/`: `IInputInjector` / `IKeepAwake` and the Windows and Linux implementations.
