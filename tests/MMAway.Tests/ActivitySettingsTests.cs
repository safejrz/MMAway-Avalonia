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
    }
}
