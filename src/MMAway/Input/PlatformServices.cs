namespace MMAway.Input;

internal static class PlatformServices
{
    public static void Initialize()
    {
        if (OperatingSystem.IsLinux())
            X11Input.InitializeThreads();
    }

    public static IGlobalHotkey CreateGlobalHotkey(string shortcut, Action pressed)
    {
        var gesture = HotkeyGesture.Parse(shortcut);
        if (OperatingSystem.IsWindows()) return new WindowsGlobalHotkey(gesture, pressed);
        if (OperatingSystem.IsLinux())
        {
            if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland",
                    StringComparison.OrdinalIgnoreCase))
                throw new PlatformNotSupportedException("Global hotkeys are not supported in Wayland sessions. Use an X11 session.");
            return new X11GlobalHotkey(gesture, pressed);
        }

        throw new PlatformNotSupportedException("Global hotkeys are supported on Windows and Linux X11.");
    }

    public static IInputInjector CreateInput()
    {
        if (OperatingSystem.IsWindows()) return new WindowsInput();
        if (OperatingSystem.IsLinux()) return new X11Input();
        throw new PlatformNotSupportedException("Only Windows and Linux (X11) are supported.");
    }

    public static IKeepAwake CreateKeepAwake()
    {
        if (OperatingSystem.IsWindows()) return new WindowsKeepAwake();
        if (OperatingSystem.IsLinux()) return new LinuxKeepAwake();
        throw new PlatformNotSupportedException("Only Windows and Linux (X11) are supported.");
    }

    /// <summary>
    /// Returns a warning when synthetic input is unlikely to work in the current session.
    /// </summary>
    public static string? GetSessionWarning()
    {
        if (OperatingSystem.IsLinux() &&
            string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
        {
            return "Wayland session detected: input only reaches XWayland apps. Log in with an X11 session for full effect.";
        }

        return null;
    }
}
