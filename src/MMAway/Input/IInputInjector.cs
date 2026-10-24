using Avalonia;

namespace MMAway.Input;

/// <summary>
/// OS-level mouse access. Coordinates are absolute screen pixels.
/// </summary>
public interface IInputInjector : IDisposable
{
    PixelPoint GetCursorPosition();

    void SetCursorPosition(PixelPoint position);

    void LeftClick();
}

/// <summary>
/// Prevents the OS from idling, sleeping or locking the screen while enabled.
/// </summary>
public interface IKeepAwake : IDisposable
{
    void Enable();

    void Disable();
}
