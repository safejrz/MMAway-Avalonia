# MMAway (Avalonia)

Cross-platform (Windows and Linux) port of [MMAway](https://github.com/safejrz/MMAway): a small tool that emulates mouse movement, clicks and typing so chat apps (e.g. MS Teams) don't flip to "Away", and keeps the machine from sleeping while it runs.

## Features
- Random mouse jitter and movement patterns within the activity text box, with a guarded click while the window is active.
- Random typing into its own text box.
- Keep-awake: `SetThreadExecutionState` on Windows, a `systemd-inhibit` idle/sleep lock on Linux.
- The window icon switches between stopped and running.
- Configurable global hotkey to start or stop activity.
- Optional auto-stop after a configured number of minutes (0 disables it).
- The main window opens near the right side of the primary display's usable area.

## Platform support
| OS | Input | Keep-awake | Global hotkey |
|----|-------|-----------|--------------|
| Windows 10/11 | `user32` (`SetCursorPos`, `mouse_event`) | `SetThreadExecutionState` | `RegisterHotKey` |
| Linux, X11 session (Mint Cinnamon, etc.) | XTest (`libX11`, `libXtst`) | `systemd-inhibit` | X11 key grab |
| Linux, Wayland session | Only reaches XWayland apps (a warning is shown) | `systemd-inhibit` | Not supported |

The default global shortcut is `Ctrl+Alt+M`. It can be changed in Options using Ctrl, Alt, Shift, or Win with A-Z, 0-9, F1-F12, Space, Enter, Esc, or Tab. If the shortcut is already taken by another app, MMAway reports that it could not register it. Configure auto-stop in Options; `0` minutes disables it.

Activity pauses when user mouse activity is detected and resumes after the configured idle delay. Cursor movement pauses while the window is minimized or the text box has no measurable bounds, and resumes when the text box is available again. Teams may therefore become Away while the window is minimized. Before a click, the app moves to the text box center and checks the actual cursor position, user input, window focus and bounds; it skips the click if a check fails. Other input or window changes can still occur between the final check and the OS-level click. On Windows, a native mouse hook ignores injected input and movement to positions injected in the last two seconds, including delayed VM/remote-desktop pointer echoes. A real click still pauses activity, even at an injected position. On X11, cursor-position polling is used. Typing begins after the initial movement pattern completes.

On Linux, install `libxtst6` if it is missing (`sudo apt install libxtst6`).

## Build and run
```bash
dotnet run --project src/MMAway
```
Run the regression tests with `dotnet test MMAway.slnx`.

## Test Windows behavior locally on Linux

The local test VM uses QEMU/KVM, OVMF secure-boot firmware and `swtpm`. On
Ubuntu or Linux Mint, install `qemu-system-x86`, `qemu-utils`, `ovmf`, `swtpm`
and `genisoimage`. Your user must have read/write access to `/dev/kvm`. Obtain
an official Windows 11 x64 installation ISO and a valid Windows license.

```bash
sudo apt install qemu-system-x86 qemu-utils ovmf swtpm genisoimage
```

1. Create a fresh Windows build and a read-only transfer CD:
   ```bash
   dotnet publish src/MMAway -c Release -r win-x64 --self-contained \
     -p:PublishSingleFile=true -o artifacts/windows-vm-share
   cp scripts/windows-idle-probe.ps1 artifacts/windows-vm-share/
   mkdir -p "$HOME/.local/share/mmaway-test-vm"
   genisoimage -quiet -J -R -o "$HOME/.local/share/mmaway-test-vm/mmaway-share.iso" artifacts/windows-vm-share
   ```
   Recreate the transfer CD while the VM is stopped after rebuilding. Do not
   place credentials or other sensitive files in the transfer directory.
2. Start the VM with `bash scripts/windows-test-vm.sh /path/to/Windows11.iso`,
   install Windows, and install Teams. Later, start it without the ISO argument.
   The VM's sparse 80 GB disk, firmware variables and TPM state persist in
   `~/.local/share/mmaway-test-vm`; do not delete them while the VM is running.
   The script allocates 4 CPUs and 8 GB RAM. `MMAWAY_VM_DIR` overrides this
   location; if set, create the transfer CD in that directory instead.
   `MMAWAY_VM_DISPLAY` selects a QEMU display backend and defaults to `gtk`.
3. In the Windows guest, copy the files from the MMAway CD to a writable
   directory. Start PowerShell in that directory and run
   `powershell -ExecutionPolicy Bypass -File .\windows-idle-probe.ps1`.
   It records session-specific `GetLastInputInfo` idle seconds to
   `mmaway-idle.csv` for 15 minutes; use a different `-CsvPath` for each run.
4. With Teams and MMAway in the same Windows session, compare runs with MMAway
   stopped, active, and minimized. Record Teams status from a separate account
   and check whether idle seconds reset and activity cycles continue. Use an
   authorized test account and avoid other active sessions for that account.

For remote-desktop comparisons, the launcher forwards guest RDP to
`127.0.0.1:33989` only; Windows must support and have RDP enabled. Keep the
session connected and unlocked. A local VM console and an RDP connection can
produce different pointer echoes, so test the connection type that showed the
problem. The probe measures Windows idle time, not Teams presence, and a
changing idle time alone does not guarantee a particular Teams status.

## Publish self-contained builds
```bash
dotnet publish src/MMAway -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64
dotnet publish src/MMAway -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o publish/win-x64
```
Pushing a `v*` tag builds both and attaches them to a GitHub release (see `.github/workflows/release.yml`).

## Layout
- `src/MMAway/ActivityEngine.cs`: platform-independent activity logic.
- `src/MMAway/Input/`: `IInputInjector` / `IKeepAwake` and the Windows and Linux implementations.
