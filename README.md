# MMAway (Avalonia)

Cross-platform (Windows and Linux) port of [MMAway](https://github.com/safejrz/MMAway): a small tool that emulates mouse movement, clicks and typing so chat apps (e.g. MS Teams) don't flip to "Away", and keeps the machine from sleeping while it runs.

## Features
- Random mouse jitter and movement patterns within the activity text box, with a guarded click while the window is active.
- Random typing into its own text box.
- Keep-awake: `SetThreadExecutionState` on Windows, a `systemd-inhibit` idle/sleep lock on Linux.
- The window icon switches between stopped and running.
- Configurable global shortcut to restore the running app, with OS setup to launch it when closed.
- Ctrl+S to start or stop activity while focused.
- Optional auto-stop after a configured number of minutes (0 disables it).
- The main window opens near the right side of the primary display's usable area.

## Platform support
| OS | Input | Keep-awake |
|----|-------|-----------|
| Windows 10/11 | `user32` (`SetCursorPos`, `mouse_event`) | `SetThreadExecutionState` |
| Linux, X11 session (Mint Cinnamon, etc.) | XTest (`libX11`, `libXtst`) | `systemd-inhibit` |
| Linux, Wayland session | Only reaches XWayland apps (a warning is shown) | `systemd-inhibit` |

### Keyboard shortcuts

Press `Ctrl+S` while the main MMAway window is focused to start or stop activity.
This shortcut does not affect other applications and cannot be used as the
global shortcut.

Choose the **Global hotkey (open MMAway)** in Options. Its default is
`Ctrl+Alt+Shift+M`. MMAway attempts to register it on Windows and Linux X11
while running; pressing it restores the window without starting or stopping
activity. Options accepts Ctrl, Alt, Shift, or Win with A-Z, 0-9, F1-F12,
Space, Enter, Esc, or Tab. If registration fails, MMAway reports the error and
retains any previously active in-app shortcut. Saving Options retries.

To launch MMAway when it is closed, also configure the same shortcut in your
operating system. Close MMAway before setting it up so its in-app registration
does not conflict. If you change the shortcut in Options, update the OS
shortcut separately:

- **Windows:** Create a shortcut to the new `MMAway.exe` on the desktop or in
  the Start menu. Open the shortcut's Properties, select the Shortcut key
  field, press your chosen shortcut, and apply. Use a published executable at a
  stable location rather than a temporary build path.
- **Linux:** In your desktop's keyboard shortcut settings, add a custom shortcut
  with your chosen shortcut and the absolute path to the published `MMAway`
  executable as its command. For Linux Mint Cinnamon, use Keyboard → Shortcuts
  → Custom Shortcuts. Choose another combination if the desktop reports a
  conflict. Available shortcuts depend on the OS and desktop environment.

The OS shortcut launches the app if it is closed. If it is already running,
the new launch requests the existing window to restore and come forward, then
exits. Focus behavior depends on the window manager. These launch shortcuts
are managed by the OS, including on Wayland; MMAway's input limitations under
Wayland still apply. When the OS already owns the shortcut, MMAway may report
that its in-app registration is unavailable; the configured OS shortcut can
still launch or restore the app. A registration failure can also mean another
application owns it, so verify the OS shortcut works or select another one.
Wayland does not support MMAway's in-app global registration.

Configure auto-stop in Options; `0` minutes disables it. The About tab shows
the application version.

Activity pauses when user mouse activity is detected and resumes after the configured idle delay. Cursor movement pauses while the window is minimized or the text box has no measurable bounds, and resumes when the text box is available again. Teams may therefore become Away while the window is minimized. Before a click, the app moves to the text box center and checks the actual cursor position, user input, window focus and bounds; it skips the click if a check fails. Other input or window changes can still occur between the final check and the OS-level click. On Windows, a native mouse hook ignores injected input and movement to positions injected in the last two seconds, including delayed VM/remote-desktop pointer echoes. A real click still pauses activity, even at an injected position. On X11, cursor-position polling is used. Typing begins after the initial movement pattern completes.

On Linux, install `libxtst6` if it is missing (`sudo apt install libxtst6`).

## Build and run
```bash
dotnet run --project src/MMAway
```
Run the regression tests with `dotnet test MMAway.slnx`.

To debug in VS Code, install the Microsoft C# extension, open the repository
folder, and select **MMAway** in Run and Debug before pressing F5. Close any
running Debug build of MMAway before starting a new build on Windows.

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
Local builds use the development version in `src/MMAway/MMAway.csproj`. Pushing a `v*` tag builds both with the tag's version and attaches them to a GitHub release (see `.github/workflows/release.yml`). After a release, update the project version to the next development version. MMAway Avalonia and the original MMAway project have separate release versions.

## Layout
- `src/MMAway/ActivityEngine.cs`: platform-independent activity logic.
- `src/MMAway/Input/`: `IInputInjector` / `IKeepAwake` and the Windows and Linux implementations.
