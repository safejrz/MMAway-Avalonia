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

    [Fact]
    public async Task LegacyGlobalHotkeyIsMigratedWithoutLosingOtherSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mmaway-settings-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"GlobalHotkey":"Ctrl+Alt+M","AutoStopMinutes":12}""");
            var store = new SettingsStore(path);
            var settings = await store.LoadAsync();
            Assert.Equal("Ctrl+Alt+Shift+M", settings.GlobalHotkey);
            Assert.Equal(12, settings.AutoStopMinutes);
            Assert.Equal("Ctrl+Alt+Shift+M", (await store.LoadAsync()).GlobalHotkey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CustomGlobalHotkeySurvivesSavingAndLoading()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mmaway-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            await store.SaveAsync(new ActivitySettings { GlobalHotkey = "Ctrl+Alt+F2" });
            Assert.Equal("Ctrl+Alt+F2", (await store.LoadAsync()).GlobalHotkey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidGlobalHotkeyIsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ActivitySettings { GlobalHotkey = "" }.Validate());
    }

    [Fact]
    public void ExistingControlSSettingLoadsButCannotBeRegisteredGlobally()
    {
        var settings = new ActivitySettings { GlobalHotkey = "control+s" };
        settings.Validate();
        Assert.ThrowsAny<ArgumentException>(() => ActivitySettings.ValidateGlobalHotkey(settings.GlobalHotkey));
    }
}
