using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace MMAway.Input;

[SupportedOSPlatform("windows")]
internal sealed class WindowsInput : IInputInjector
{
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    public PixelPoint GetCursorPosition() =>
        GetCursorPos(out var p) ? new PixelPoint(p.X, p.Y) : default;

    public void SetCursorPosition(PixelPoint position) => SetCursorPos(position.X, position.Y);

    public void LeftClick() =>
        mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

    public void Dispose()
    {
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
