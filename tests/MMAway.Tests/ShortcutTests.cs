using Avalonia.Input;

namespace MMAway.Tests;

public sealed class ShortcutTests
{
    [Theory]
    [InlineData(Key.S, KeyModifiers.Control, true)]
    [InlineData(Key.S, KeyModifiers.Control | KeyModifiers.Shift, false)]
    [InlineData(Key.S, KeyModifiers.Alt, false)]
    [InlineData(Key.M, KeyModifiers.Control, false)]
    public void ToggleRequiresExactlyControlS(Key key, KeyModifiers modifiers, bool expected) =>
        Assert.Equal(expected, MainWindow.IsToggleShortcut(key, modifiers));

    [Fact]
    public async Task LaterLaunchesActivateThePrimaryInstance()
    {
        var name = $"MMAway-test-{Guid.NewGuid():N}";
        using var primary = new SingleInstance(name);
        Assert.True(primary.IsPrimary);
        var activations = 0;
        Exception? listenerError = null;
        primary.StartListening(() => Interlocked.Increment(ref activations), ex => listenerError = ex);

        using (var second = new SingleInstance(name))
        {
            Assert.False(second.IsPrimary);
            await second.ActivatePrimaryAsync();
            await second.ActivatePrimaryAsync();
        }

        Assert.Equal(2, activations);
        Assert.Null(listenerError);
    }
}
