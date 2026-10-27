using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace MMAway.Input;

[SupportedOSPlatform("windows")]
internal sealed class WindowsInput : IInputInjector
{
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const int MouseHook = 14;
    private const int MouseMoveMessage = 0x0200;
    private const uint InjectedFlag = 0x0001;
    private readonly MouseActivityTracker activity = new();
    private readonly HookCallback hookCallback;
    private IntPtr hook;

    private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public POINT Position;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookCallback callback, IntPtr module, uint thread);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    // Constructed on the UI thread, whose message loop delivers low-level hook callbacks.
    public WindowsInput()
    {
        hookCallback = OnMouseInput;
        hook = SetWindowsHookEx(MouseHook, hookCallback, IntPtr.Zero, 0);
        if (hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not monitor user mouse activity.");
    }

    public uint? UserInputVersion => activity.Version;

    private IntPtr OnMouseInput(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var mouse = Marshal.PtrToStructure<MouseHookData>(data);
            activity.RecordMouseEvent(new PixelPoint(mouse.Position.X, mouse.Position.Y),
                (mouse.Flags & InjectedFlag) != 0, message == new IntPtr(MouseMoveMessage));
        }
        return CallNextHookEx(hook, code, message, data);
    }

    public PixelPoint GetCursorPosition()
    {
        if (!GetCursorPos(out var p))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the cursor position.");
        return new PixelPoint(p.X, p.Y);
    }

    public void SetCursorPosition(PixelPoint position)
    {
        activity.RecordInjection(position);
        if (!SetCursorPos(position.X, position.Y))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not move the cursor.");
        var actual = GetCursorPosition();
        if (actual != position) activity.RecordInjection(actual);
    }

    public void LeftClick() =>
        mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

    public void Dispose()
    {
        if (hook == IntPtr.Zero) return;
        if (!UnhookWindowsHookEx(hook))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not stop monitoring user mouse activity.");
        hook = IntPtr.Zero;
    }
}

internal sealed class MouseActivityTracker
{
    private readonly object gate = new();
    private readonly Queue<(PixelPoint Position, long Time)> injections = new();
    private int version;

    public uint Version => unchecked((uint)Volatile.Read(ref version));

    public void RecordInjection(PixelPoint position)
    {
        lock (gate)
        {
            RemoveExpiredInjections();
            injections.Enqueue((position, Environment.TickCount64));
        }
    }

    public void RecordMouseEvent(PixelPoint position, bool injected, bool movement)
    {
        if (injected) return;
        if (movement)
        {
            lock (gate)
            {
                RemoveExpiredInjections();
                // RDP can echo a warp as an unmarked mouse move after the native call returns.
                if (injections.Any(injection => injection.Position == position)) return;
            }
        }
        Interlocked.Increment(ref version);
    }

    private void RemoveExpiredInjections()
    {
        var cutoff = Environment.TickCount64 - 2000;
        while (injections.TryPeek(out var injection) && injection.Time < cutoff)
            injections.Dequeue();
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsKeepAwake : IKeepAwake
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(ExecutionState flags);

    // The execution state is per-thread, so Enable/Disable must be called from the same (UI) thread.
    public void Enable() =>
        SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.DisplayRequired);

    public void Disable() => SetThreadExecutionState(ExecutionState.Continuous);

    public void Dispose() => Disable();
}
