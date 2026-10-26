using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace MMAway;

public partial class OptionsWindow : Window
{
    private readonly Dictionary<string, CheckBox> patternCheckBoxes = new(StringComparer.Ordinal);
    private CheckBox randomPatternsCheckBox = null!;
    private bool updatingPatternChecks;

    public ActivitySettings? SavedSettings { get; private set; }

    public OptionsWindow() : this(new ActivitySettings())
    {
    }

    public OptionsWindow(ActivitySettings settings)
    {
        InitializeComponent();
        BuildPatternChoices();
        SetValues(settings);
        AggressivenessSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                AggressivenessValue.Text = $"{AggressivenessSlider.Value:0.0}x";
        };
    }

    private void SetValues(ActivitySettings settings)
    {
        MinimumIntervalBox.Text = settings.IntervalMinimumSeconds.ToString();
        MaximumIntervalBox.Text = settings.IntervalMaximumSeconds.ToString();
        TypingEnabledBox.IsChecked = settings.TypingEnabled;
        AggressivenessSlider.Value = settings.Aggressiveness;
        AggressivenessValue.Text = $"{settings.Aggressiveness:0.0}x";
        AutoStopBox.Text = settings.AutoStopMinutes.ToString();
        HotkeyBox.Text = settings.GlobalHotkey;

        updatingPatternChecks = true;
        foreach (var (name, checkBox) in patternCheckBoxes)
            checkBox.IsChecked = settings.SelectedPatterns.Contains(name, StringComparer.Ordinal);
        randomPatternsCheckBox.IsChecked = settings.SelectedPatterns.Count == patternCheckBoxes.Count;
        updatingPatternChecks = false;
        UpdatePatternButtonText();
    }

    private void BuildPatternChoices()
    {
        randomPatternsCheckBox = new CheckBox { Content = "Random (all patterns)" };
        randomPatternsCheckBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                OnRandomPatternsChanged();
        };
        PatternChoices.Children.Add(randomPatternsCheckBox);

        foreach (var pattern in Enum.GetValues<MovementPattern>())
        {
            var checkBox = new CheckBox
            {
                Content = GetPatternLabel(pattern),
                Tag = pattern.ToString()
            };
            checkBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == ToggleButton.IsCheckedProperty)
                    OnPatternChanged();
            };
            patternCheckBoxes.Add(pattern.ToString(), checkBox);
            PatternChoices.Children.Add(checkBox);
        }
    }

    private void OnRandomPatternsChanged()
    {
        if (updatingPatternChecks) return;
        updatingPatternChecks = true;
        foreach (var checkBox in patternCheckBoxes.Values)
            checkBox.IsChecked = randomPatternsCheckBox.IsChecked == true;
        updatingPatternChecks = false;
        UpdatePatternButtonText();
    }

    private void OnPatternChanged()
    {
        if (updatingPatternChecks) return;
        var allSelected = patternCheckBoxes.Values.All(checkBox => checkBox.IsChecked == true);
        updatingPatternChecks = true;
        randomPatternsCheckBox.IsChecked = allSelected;
        updatingPatternChecks = false;
        UpdatePatternButtonText();
    }

    private void UpdatePatternButtonText()
    {
        var selected = patternCheckBoxes.Values.Count(checkBox => checkBox.IsChecked == true);
        PatternButton.Content = selected == patternCheckBoxes.Count
            ? "Random (all patterns)"
            : selected == 1
                ? patternCheckBoxes.Values.First(checkBox => checkBox.IsChecked == true).Content
                : $"{selected} patterns selected";
    }

    private static string GetPatternLabel(MovementPattern pattern) => pattern switch
    {
        MovementPattern.SpiralOut => "Spiral outward",
        MovementPattern.SpiralIn => "Spiral inward",
        MovementPattern.IceCreamCone => "Ice-cream cone",
        MovementPattern.FigureEight => "Figure eight",
        MovementPattern.VerticalHourglass => "Vertical hourglass",
        MovementPattern.HorizontalHourglass => "Horizontal hourglass",
        _ => pattern.ToString()
    };

    private void OnPatternButtonClick(object? sender, RoutedEventArgs e) =>
        PatternPopup.IsOpen = !PatternPopup.IsOpen;

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        if (!int.TryParse(MinimumIntervalBox.Text, out var minimum) ||
            !int.TryParse(MaximumIntervalBox.Text, out var maximum) ||
            !int.TryParse(AutoStopBox.Text, out var autoStop))
        {
            ShowError("Enter whole numbers for the interval and auto-stop fields.");
            return;
        }

        var settings = new ActivitySettings
        {
            IntervalMinimumSeconds = minimum,
            IntervalMaximumSeconds = maximum,
            SelectedPatterns = patternCheckBoxes
                .Where(pair => pair.Value.IsChecked == true)
                .Select(pair => pair.Key)
                .ToList(),
            TypingEnabled = TypingEnabledBox.IsChecked ?? false,
            Aggressiveness = AggressivenessSlider.Value,
            AutoStopMinutes = autoStop,
            GlobalHotkey = HotkeyBox.Text?.Trim() ?? string.Empty
        };

        try
        {
            settings.Validate();
            SavedSettings = settings;
            Close(true);
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e) => SetValues(new ActivitySettings());

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
