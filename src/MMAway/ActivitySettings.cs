namespace MMAway;

using MMAway.Input;

public sealed record ActivitySettings
{
    public int IntervalMinimumSeconds { get; init; } = 4;
    public int IntervalMaximumSeconds { get; init; } = 14;
    public int TypingIntervalMinimumSeconds { get; init; } = 2;
    public int TypingIntervalMaximumSeconds { get; init; } = 12;
    public int TypingBurstMinimumCharacters { get; init; } = 2;
    public int TypingBurstMaximumCharacters { get; init; } = 20;
    public bool TypingSpacesEnabled { get; init; } = true;
    public int SpaceMinimumCharacters { get; init; } = 0;
    public int SpaceMaximumCharacters { get; init; } = 6;
    public List<string> SelectedPatterns { get; init; } = [.. Enum.GetNames<MovementPattern>()];
    public bool TypingEnabled { get; init; } = true;
    public double Aggressiveness { get; init; } = 1.5;
    public int UserIdleSeconds { get; init; } = 10;
    public bool ShowSessionInfo { get; init; }
    public int AutoStopMinutes { get; init; }
    public string GlobalHotkey { get; init; } = "Ctrl+Alt+M";

    public void Validate()
    {
        if (IntervalMinimumSeconds is < 1 or > 300)
            throw new ArgumentOutOfRangeException(nameof(IntervalMinimumSeconds), "Minimum interval must be between 1 and 300 seconds.");
        if (IntervalMaximumSeconds < IntervalMinimumSeconds || IntervalMaximumSeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(IntervalMaximumSeconds), "Maximum interval must be at least the minimum and no more than 300 seconds.");
        if (TypingIntervalMinimumSeconds is < 1 or > 300)
            throw new ArgumentOutOfRangeException(nameof(TypingIntervalMinimumSeconds), "Minimum typing interval must be between 1 and 300 seconds.");
        if (TypingIntervalMaximumSeconds < TypingIntervalMinimumSeconds || TypingIntervalMaximumSeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(TypingIntervalMaximumSeconds), "Maximum typing interval must be at least the minimum and no more than 300 seconds.");
        if (TypingBurstMinimumCharacters is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(TypingBurstMinimumCharacters), "Minimum burst size must be between 1 and 100 characters.");
        if (TypingBurstMaximumCharacters < TypingBurstMinimumCharacters || TypingBurstMaximumCharacters > 100)
            throw new ArgumentOutOfRangeException(nameof(TypingBurstMaximumCharacters), "Maximum burst size must be at least the minimum and no more than 100 characters.");
        if (SpaceMinimumCharacters is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(SpaceMinimumCharacters), "Minimum characters between spaces must be between 0 and 100.");
        if (SpaceMaximumCharacters < SpaceMinimumCharacters || SpaceMaximumCharacters > 100)
            throw new ArgumentOutOfRangeException(nameof(SpaceMaximumCharacters), "Maximum characters between spaces must be at least the minimum and no more than 100.");
        if (SelectedPatterns.Count == 0)
            throw new ArgumentException("Select at least one movement pattern.", nameof(SelectedPatterns));
        if (SelectedPatterns.Distinct(StringComparer.Ordinal).Count() != SelectedPatterns.Count ||
            SelectedPatterns.Any(pattern => !Enum.GetNames<MovementPattern>().Contains(pattern, StringComparer.Ordinal)))
            throw new ArgumentException("Movement patterns must be unique supported pattern names.", nameof(SelectedPatterns));
        if (!double.IsFinite(Aggressiveness) || Aggressiveness is < 0.5 or > 3.0)
            throw new ArgumentOutOfRangeException(nameof(Aggressiveness), "Movement speed must be between 0.5x and 3.0x.");
        if (UserIdleSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(UserIdleSeconds), "Resume delay must be between 1 and 3600 seconds.");
        if (AutoStopMinutes is < 0 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(AutoStopMinutes), "Auto-stop must be between 0 and 1440 minutes.");
        if (string.IsNullOrWhiteSpace(GlobalHotkey))
            throw new ArgumentException("Global hotkey cannot be empty.", nameof(GlobalHotkey));
        _ = HotkeyGesture.Parse(GlobalHotkey);
    }
}
