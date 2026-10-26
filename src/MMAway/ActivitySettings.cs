namespace MMAway;

public sealed record ActivitySettings
{
    public int IntervalMinimumSeconds { get; init; } = 5;
    public int IntervalMaximumSeconds { get; init; } = 15;
    public List<string> SelectedPatterns { get; init; } = [.. Enum.GetNames<MovementPattern>()];
    public bool TypingEnabled { get; init; } = true;
    public double Aggressiveness { get; init; } = 1.0;
    public int UserIdleSeconds { get; init; } = 45;
    public bool ShowSessionInfo { get; init; }
    public int AutoStopMinutes { get; init; }
    public string GlobalHotkey { get; init; } = "Ctrl+Alt+M";

    public void Validate()
    {
        if (IntervalMinimumSeconds is < 1 or > 300)
            throw new ArgumentOutOfRangeException(nameof(IntervalMinimumSeconds), "Minimum interval must be between 1 and 300 seconds.");
        if (IntervalMaximumSeconds < IntervalMinimumSeconds || IntervalMaximumSeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(IntervalMaximumSeconds), "Maximum interval must be at least the minimum and no more than 300 seconds.");
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
    }
}
