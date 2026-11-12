using MMAway.Input;

namespace MMAway.Tests;

public sealed class GlobalHotkeyTests
{
    [Fact]
    public void EquivalentShortcutsDoNotReregister()
    {
        var calls = 0;
        using var hotkey = new HotkeyRegistration((_, _) =>
        {
            calls++;
            return new FakeHotkey();
        }, () => { });

        hotkey.Configure("Ctrl+Alt+Shift+M");
        hotkey.Configure("shift+control+alt+m");

        Assert.Equal(1, calls);
    }

    [Fact]
    public void FailedReplacementKeepsOldShortcutAndCanBeRetried()
    {
        var first = new FakeHotkey();
        var second = new FakeHotkey();
        var calls = 0;
        using var hotkey = new HotkeyRegistration((_, _) => ++calls switch
        {
            1 => first,
            2 => throw new InvalidOperationException("Shortcut in use"),
            _ => second
        }, () => { });

        hotkey.Configure("Ctrl+Alt+M");
        Assert.Throws<InvalidOperationException>(() => hotkey.Configure("Ctrl+Alt+Shift+M"));
        Assert.Equal("Ctrl+Alt+M", hotkey.ActiveShortcut);
        Assert.False(first.Disposed);

        hotkey.Configure("Ctrl+Alt+Shift+M");
        Assert.True(first.Disposed);
        Assert.Equal("Ctrl+Alt+Shift+M", hotkey.ActiveShortcut);
    }

    [Fact]
    public void X11ModifierMatchIgnoresLockAndMouseButtonStates()
    {
        if (!OperatingSystem.IsLinux()) return;
        const uint modifiers = 1 | 4 | 8;
        Assert.True(X11GlobalHotkey.MatchesModifiers(modifiers | 2 | 16 | 256, modifiers));
        Assert.False(X11GlobalHotkey.MatchesModifiers(modifiers | 64, modifiers));
    }

    private sealed class FakeHotkey : IGlobalHotkey
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
