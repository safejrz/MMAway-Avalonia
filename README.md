# MMAway (Avalonia)

Cross-platform (Windows and Linux) port of [MMAway](https://github.com/safejrz/MMAway): a small tool that emulates mouse movement, clicks and typing so chat apps (e.g. MS Teams) don't flip to "Away", and keeps the machine from sleeping while it runs.

## Features
- Random mouse jitter, square / figure-eight patterns and a click on its own window.
- Random typing into its own text box.
- Keep-awake: `SetThreadExecutionState` on Windows, a `systemd-inhibit` idle/sleep lock on Linux.
- The window icon switches between stopped and running.

## Platform support
| OS | Input | Keep-awake |
|----|-------|-----------|
| Windows 10/11 | `user32` (`SetCursorPos`, `mouse_event`) | `SetThreadExecutionState` |
| Linux, X11 session (Mint Cinnamon, etc.) | XTest (`libX11`, `libXtst`) | `systemd-inhibit` |
| Linux, Wayland session | Only reaches XWayland apps (a warning is shown) | `systemd-inhibit` |

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
