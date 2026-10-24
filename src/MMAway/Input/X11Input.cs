using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace MMAway.Input;

/// <summary>
/// Mouse injection on X11 through the XTest extension.
/// Under Wayland this only reaches XWayland clients.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11Input : IInputInjector
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXtst = "libXtst.so.6";

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(string? name);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);

    [DllImport(LibX11)]
    private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
        out int rootX, out int rootY, out int winX, out int winY, out uint mask);

    [DllImport(LibXtst)]
    private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, ulong delay);

    [DllImport(LibXtst)]
    private static extern int XTestFakeButtonEvent(IntPtr display, uint button, bool isPress, ulong delay);

    private readonly object gate = new();
    private readonly IntPtr display;
    private readonly IntPtr root;

    public X11Input()
    {
        display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot open the X11 display. Is DISPLAY set?");
        }

        root = XDefaultRootWindow(display);
    }

    public PixelPoint GetCursorPosition()
    {
        lock (gate)
        {
            XQueryPointer(display, root, out _, out _, out var x, out var y, out _, out _, out _);
            return new PixelPoint(x, y);
        }
    }

    public void SetCursorPosition(PixelPoint position)
    {
        lock (gate)
        {
            // Screen -1 means "the screen the pointer is currently on".
            XTestFakeMotionEvent(display, -1, position.X, position.Y, 0);
            XFlush(display);
        }
    }

    public void LeftClick()
    {
        lock (gate)
        {
            XTestFakeButtonEvent(display, 1, true, 0);
            XTestFakeButtonEvent(display, 1, false, 0);
            XFlush(display);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            XCloseDisplay(display);
        }
    }
}

/// <summary>
/// Blocks idle and sleep by holding a systemd-logind inhibitor lock for as long as it is enabled.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxKeepAwake : IKeepAwake
{
    private Process? inhibitor;

    public void Enable()
    {
        if (inhibitor is { HasExited: false })
        {
            return;
        }

        try
        {
            inhibitor = Process.Start(new ProcessStartInfo
            {
                FileName = "systemd-inhibit",
                ArgumentList =
                {
                    "--what=idle:sleep", "--who=MMAway", "--why=Keeping the session active",
                    "--mode=block", "sleep", "infinity"
                },
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception)
        {
            // systemd-inhibit is not available; the synthetic input still keeps the session from idling.
            inhibitor = null;
        }
    }

    public void Disable()
    {
        try
        {
            if (inhibitor is { HasExited: false })
            {
                inhibitor.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        inhibitor?.Dispose();
        inhibitor = null;
    }

    public void Dispose() => Disable();
}
