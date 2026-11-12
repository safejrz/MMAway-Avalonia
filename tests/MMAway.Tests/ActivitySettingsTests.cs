namespace MMAway.Tests;

public sealed class ActivitySettingsTests
{
    [Fact]
    public void DefaultsMatchCurrentSettings()
    {
        var settings = new ActivitySettings();

        Assert.Equal(4, settings.IntervalMinimumSeconds);
        Assert.Equal(14, settings.IntervalMaximumSeconds);
        Assert.Equal(2, settings.TypingIntervalMinimumSeconds);
        Assert.Equal(12, settings.TypingIntervalMaximumSeconds);
        Assert.Equal(1.5, settings.Aggressiveness);
        Assert.Equal("Ctrl+Alt+Shift+M", settings.GlobalHotkey);
    }

    [Theory]
    [InlineData("Ctrl+Alt+M", "Ctrl+Alt+Shift+M")]
    [InlineData("Alt+Control+M", "Ctrl+Alt+Shift+M")]
    [InlineData("Ctrl+Alt+F1", "Ctrl+Alt+F1")]
    public async Task LegacySettingsMigrateOnlyTheOldDefault(string shortcut, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mmaway-settings-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, $$"""{"GlobalHotkey":"{{shortcut}}"}""");
            var store = new SettingsStore(path);
            Assert.Equal(expected, (await store.LoadAsync()).GlobalHotkey);
            Assert.Equal(expected, (await store.LoadAsync()).GlobalHotkey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SavedOldShortcutIsNotMigratedAgain()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mmaway-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            await store.SaveAsync(new ActivitySettings { GlobalHotkey = "Ctrl+Alt+M" });
            Assert.Equal("Ctrl+Alt+M", (await store.LoadAsync()).GlobalHotkey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
