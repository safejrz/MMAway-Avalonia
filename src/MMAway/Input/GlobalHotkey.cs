using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MMAway.Input;

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Super = 8
}

internal readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public static HotkeyGesture Parse(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new ArgumentException("Use a modifier and key, for example Ctrl+Alt+M.", nameof(value));

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "super" or "meta" => HotkeyModifiers.Super,
                _ => throw new ArgumentException($"Unsupported hotkey modifier '{part}'.", nameof(value))
            };
            if ((modifiers & modifier) != 0)
                throw new ArgumentException($"Duplicate hotkey modifier '{part}'.", nameof(value));
            modifiers |= modifier;
        }

        var key = parts[^1].ToUpperInvariant();
        if (parts[^1].Length != 1 && key is not ("SPACE" or "ENTER" or "ESC" or "TAB") &&
            !(key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 12))
            throw new ArgumentException($"Unsupported hotkey key '{parts[^1]}'. Use A-Z, 0-9, F1-F12, Space, Enter, Esc, or Tab.", nameof(value));
        if (parts[^1].Length == 1 && !char.IsAsciiLetterOrDigit(parts[^1][0]))
            throw new ArgumentException($"Unsupported hotkey key '{parts[^1]}'.", nameof(value));

        return new HotkeyGesture(modifiers, key);
    }

    public uint WindowsVirtualKey => Key switch
    {
        "SPACE" => 0x20,
        "ENTER" => 0x0D,
        "ESC" => 0x1B,
        "TAB" => 0x09,
        _ when Key.Length > 1 && Key[0] == 'F' => 0x70u + uint.Parse(Key[1..]) - 1,
        _ => (uint)Key[0]
    };

    public nuint X11Keysym => Key switch
    {
        "SPACE" => 0x20,
        "ENTER" => 0xFF0D,
        "ESC" => 0xFF1B,
        "TAB" => 0xFF09,
        _ when Key.Length > 1 && Key[0] == 'F' => 0xFFBDu + uint.Parse(Key[1..]),
        _ => char.ToLowerInvariant(Key[0])
    };
}

internal interface IGlobalHotkey : IDisposable
{
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsGlobalHotkey : IGlobalHotkey
{
    private const int HotkeyId = 0x4D4D;
    private const uint HotkeyMessage = 0x0312;
    private const uint QuitMessage = 0x0012;
    private const uint PeekNoRemove = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Message message, IntPtr window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, IntPtr window, uint minimum, uint maximum, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly Action pressed;
    private Exception? registrationError;
    private uint threadId;
    private volatile bool stopping;

    public WindowsGlobalHotkey(HotkeyGesture gesture, Action pressed)
    {
        this.pressed = pressed;
        thread = new Thread(() => Run(gesture)) { IsBackground = true, Name = "MMAway global hotkey" };
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            Dispose();
            throw new TimeoutException("Timed out while registering the global hotkey.");
        }
        if (registrationError is not null)
        {
            Dispose();
            throw registrationError;
        }
    }

    private void Run(HotkeyGesture gesture)
    {
        threadId = GetCurrentThreadId();
        try
        {
            _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PeekNoRemove);
            if (stopping) return;
            if (!RegisterHotKey(IntPtr.Zero, HotkeyId, ToWindowsModifiers(gesture.Modifiers), gesture.WindowsVirtualKey))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The global hotkey could not be registered (it may already be in use).");
            ready.Set();

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Id == HotkeyMessage)
                    pressed();
            }
        }
        catch (Exception ex)
        {
            registrationError = ex;
            ready.Set();
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    private static uint ToWindowsModifiers(HotkeyModifiers modifiers)
    {
        uint result = 0x4000;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= 0x0002;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= 0x0001;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= 0x0004;
        if (modifiers.HasFlag(HotkeyModifiers.Super)) result |= 0x0008;
        return result;
    }

    public void Dispose()
    {
        stopping = true;
        if (threadId != 0)
            _ = PostThreadMessage(threadId, QuitMessage, UIntPtr.Zero, IntPtr.Zero);
        if (Thread.CurrentThread != thread)
            thread.Join(TimeSpan.FromSeconds(2));
        if (!thread.IsAlive) ready.Dispose();
    }
}

[SupportedOSPlatform("linux")]
internal sealed class X11GlobalHotkey : IGlobalHotkey
{
    private const int KeyPress = 2;
    private const uint LockMask = 2;
    private const uint Mod2Mask = 16;
    private const int GrabModeAsync = 1;
    private const int XEventSize = 192;

    [StructLayout(LayoutKind.Explicit, Size = XEventSize)]
    private struct XEvent
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(32)] public IntPtr Window;
        [FieldOffset(64)] public int RootX;
        [FieldOffset(68)] public int RootY;
        [FieldOffset(72)] public int X;
        [FieldOffset(76)] public int Y;
        [FieldOffset(80)] public uint State;
        [FieldOffset(84)] public uint Keycode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XErrorEvent
    {
        public int Type;
        public IntPtr Display;
        public UIntPtr Resource;
        public UIntPtr Serial;
        public byte ErrorCode;
        public byte RequestCode;
        public byte MinorCode;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, ref XErrorEvent error);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(string? name);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern byte XKeysymToKeycode(IntPtr display, UIntPtr keysym);

    [DllImport("libX11.so.6")]
    private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window,
        int ownerEvents, int pointerMode, int keyboardMode);

    [DllImport("libX11.so.6")]
    private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XSync(IntPtr display, bool discard);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(IntPtr display, out XEvent xEvent);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);

    private static readonly object ErrorHandlerGate = new();
    private static readonly XErrorHandler ErrorHandler = CaptureXError;
    private static IntPtr errorDisplay;
    private static int lastXError;
    private static IntPtr previousErrorHandler;

    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly Action pressed;
    private volatile bool stopping;
    private Exception? registrationError;
    private IntPtr display;
    private IntPtr root;
    private byte keycode;
    private uint modifiers;

    public X11GlobalHotkey(HotkeyGesture gesture, Action pressed)
    {
        this.pressed = pressed;
        thread = new Thread(() => Run(gesture)) { IsBackground = true, Name = "MMAway global hotkey" };
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            Dispose();
            throw new TimeoutException("Timed out while registering the global hotkey.");
        }
        if (registrationError is not null)
        {
            Dispose();
            throw registrationError;
        }
    }

    private void Run(HotkeyGesture gesture)
    {
        try
        {
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
                throw new InvalidOperationException("Cannot connect to X11 for global hotkey registration.");

            root = XDefaultRootWindow(display);
            keycode = XKeysymToKeycode(display, gesture.X11Keysym);
            if (keycode == 0)
                throw new ArgumentException($"The key '{gesture.Key}' is not available in the X11 keymap.");
            modifiers = ToX11Modifiers(gesture.Modifiers);

            var grabs = new List<uint>();
            for (var lockVariant = 0; lockVariant < 4; lockVariant++)
            {
                var locks = (lockVariant & 1) != 0 ? LockMask : 0;
                locks |= (lockVariant & 2) != 0 ? Mod2Mask : 0;
                var grabModifiers = modifiers | locks;
                XGrabKey(display, keycode, grabModifiers, root, 0, GrabModeAsync, GrabModeAsync);
                grabs.Add(grabModifiers);
            }

            int xError;
            lock (ErrorHandlerGate)
            {
                errorDisplay = display;
                lastXError = 0;
                previousErrorHandler = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(ErrorHandler));
                XSync(display, false);
                xError = lastXError;
                _ = XSetErrorHandler(previousErrorHandler);
                errorDisplay = IntPtr.Zero;
            }
            if (xError != 0)
            {
                foreach (var grab in grabs) XUngrabKey(display, keycode, grab, root);
                XSync(display, false);
                throw new InvalidOperationException("The global hotkey could not be grabbed (it may already be in use).");
            }

            ready.Set();
            while (!stopping)
            {
                while (XPending(display) > 0)
                {
                    XNextEvent(display, out var xEvent);
                    if (xEvent.Type == KeyPress && xEvent.Keycode == keycode &&
                        (xEvent.State & ~(LockMask | Mod2Mask)) == modifiers)
                        pressed();
                }
                Thread.Sleep(20);
            }

            foreach (var grab in grabs) XUngrabKey(display, keycode, grab, root);
            XSync(display, false);
        }
        catch (Exception ex)
        {
            registrationError = ex;
            ready.Set();
        }
        finally
        {
            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }
    }

    private static int CaptureXError(IntPtr display, ref XErrorEvent error)
    {
        if (display == errorDisplay)
        {
            lastXError = error.ErrorCode;
            return 0;
        }

        if (previousErrorHandler != IntPtr.Zero)
            return Marshal.GetDelegateForFunctionPointer<XErrorHandler>(previousErrorHandler)(display, ref error);
        return 0;
    }

    private static uint ToX11Modifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= 1;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= 4;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= 8;
        if (modifiers.HasFlag(HotkeyModifiers.Super)) result |= 64;
        return result;
    }

    public void Dispose()
    {
        stopping = true;
        if (Thread.CurrentThread != thread)
            thread.Join(TimeSpan.FromSeconds(2));
        if (!thread.IsAlive) ready.Dispose();
    }
}
