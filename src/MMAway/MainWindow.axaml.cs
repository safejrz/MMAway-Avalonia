using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MMAway.Input;

namespace MMAway;

public partial class MainWindow : Window
{
    private readonly WindowIcon runIcon = LoadIcon("RunIcon.ico");
    private readonly WindowIcon stopIcon = LoadIcon("StopIcon.ico");
    private IInputInjector? input;
    private IKeepAwake? keepAwake;
    private ActivityEngine? engine;
    private readonly SettingsStore settingsStore = new();
    private ActivitySettings settings = new();
    private string sessionWarning = string.Empty;
    private string hotkeyWarning = string.Empty;
    private readonly RotateTransform leftSpinnerRotation = new();
    private readonly RotateTransform rightSpinnerRotation = new();
    private readonly DispatcherTimer spinnerTimer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly DispatcherTimer sessionStatsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer autoStopTimer = new();
    private IGlobalHotkey? globalHotkey;
    private bool isStopping;

    public MainWindow()
    {
        InitializeComponent();
        SpinnerLeft.RenderTransform = leftSpinnerRotation;
        SpinnerRight.RenderTransform = rightSpinnerRotation;
        spinnerTimer.Tick += (_, _) =>
        {
            leftSpinnerRotation.Angle = (leftSpinnerRotation.Angle + 6) % 360;
            rightSpinnerRotation.Angle = (rightSpinnerRotation.Angle + 6) % 360;
        };
        sessionStatsTimer.Tick += (_, _) => UpdateRunningStats();
        autoStopTimer.Tick += OnAutoStopTimerTick;
        Opened += OnOpened;
        Closing += OnClosing;
        Activated += (_, _) => UpdateWindowBounds();
        Deactivated += (_, _) => UpdateWindowBounds();
        PositionChanged += (_, _) => UpdateWindowBounds();
        Resized += (_, _) => UpdateWindowBounds();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateWindowBounds();
        };
    }

    // The engine thread must not touch Avalonia properties, so the effective bounds are
    // resolved on the UI thread and published as a single immutable reference.
    private sealed record BoundsSnapshot(PixelRect Rect, PixelPoint OutputCenter, bool CanClick);

    private BoundsSnapshot currentBounds =
        new(new PixelRect(0, 0, 1024, 768), new PixelPoint(512, 384), false);
    private PixelRect screenBounds = new(0, 0, 1024, 768);

    // Falls back to the screen when the window is minimized or not measured yet.
    private void UpdateWindowBounds()
    {
        var origin = this.PointToScreen(new Point(0, 0));
        var scale = RenderScaling;
        var rect = new PixelRect(origin.X, origin.Y,
            (int)(ClientSize.Width * scale), (int)(ClientSize.Height * scale));
        var useScreen = WindowState == WindowState.Minimized || rect.Width <= 0 || rect.Height <= 0;
        var outputCenter = OutputBox.Bounds.Width > 0 && OutputBox.Bounds.Height > 0
            ? OutputBox.PointToScreen(new Point(OutputBox.Bounds.Width / 2, OutputBox.Bounds.Height / 2))
            : rect.Center;
        Volatile.Write(ref currentBounds,
            new BoundsSnapshot(useScreen ? screenBounds : rect, outputCenter,
                IsActive && WindowState != WindowState.Minimized));
    }

    private PixelRect GetBounds() => Volatile.Read(ref currentBounds).Rect;
    private PixelPoint GetOutputCenter() => Volatile.Read(ref currentBounds).OutputCenter;
    private bool CanClick() => Volatile.Read(ref currentBounds).CanClick;

    private static WindowIcon LoadIcon(string name) =>
        new(Avalonia.Platform.AssetLoader.Open(new Uri($"avares://MMAway/Assets/{name}")));

    private async void OnOpened(object? sender, EventArgs e)
    {
        var bounds = (Screens.Primary ?? Screens.All.FirstOrDefault())?.Bounds ?? new PixelRect(0, 0, 1024, 768);
        Position = new PixelPoint(bounds.X + bounds.Width / 10, bounds.Y + bounds.Height / 5);
        screenBounds = bounds;
        UpdateWindowBounds();

        try
        {
            settings = await settingsStore.LoadAsync();
            input = PlatformServices.CreateInput();
            keepAwake = PlatformServices.CreateKeepAwake();
            engine = new ActivityEngine(
                input,
                GetBounds,
                GetOutputCenter,
                CanClick,
                () => Volatile.Read(ref settings),
                text => Dispatcher.UIThread.Post(() => AppendOutputText(text)),
                () => Dispatcher.UIThread.Post(SelectRandomOutputText),
                () => Dispatcher.UIThread.Post(() => OutputBox.Clear()));
        }
        catch (Exception ex)
        {
            ToggleButton.IsEnabled = false;
            SetStatusText(ex.Message);
            return;
        }

        hotkeyWarning = TryConfigureGlobalHotkey(settings.GlobalHotkey) ?? string.Empty;
        sessionWarning = PlatformServices.GetSessionWarning() ?? string.Empty;
        SetStatusText(GetCurrentWarning());
    }

    private void AppendOutputText(string text)
    {
        var content = OutputBox.Text ?? string.Empty;
        var start = Math.Clamp(OutputBox.SelectionStart, 0, content.Length);
        var end = Math.Clamp(OutputBox.SelectionEnd, 0, content.Length);
        if (start > end) (start, end) = (end, start);

        var insertAt = end > start ? start : content.Length;
        var removeLength = end - start;
        OutputBox.Text = content.Remove(insertAt, removeLength).Insert(insertAt, text);
        OutputBox.CaretIndex = insertAt + text.Length;
    }

    private void SelectRandomOutputText()
    {
        var length = OutputBox.Text?.Length ?? 0;
        if (length == 0) return;

        var start = Random.Shared.Next(length);
        var selectedLength = Random.Shared.Next(1, Math.Min(12, length - start) + 1);
        OutputBox.SelectionStart = start;
        OutputBox.SelectionEnd = start + selectedLength;
    }

    private async void OnToggleClick(object? sender, RoutedEventArgs e) =>
        await ToggleActivityAsync();

    private async Task ToggleActivityAsync()
    {
        if (engine is null || keepAwake is null || input is null) return;

        if (engine.IsRunning)
        {
            await StopActivityAsync();
            return;
        }

        OptionsButton.IsEnabled = false;
        keepAwake.Enable();
        SetRunningVisuals(true);
        Icon = runIcon;
        ToggleButton.Content = "Stop!";
        engine.Start();
        if (settings.AutoStopMinutes > 0)
        {
            autoStopTimer.Interval = TimeSpan.FromMinutes(settings.AutoStopMinutes);
            autoStopTimer.Start();
        }
    }

    private async void OnAutoStopTimerTick(object? sender, EventArgs e)
    {
        autoStopTimer.Stop();
        await StopActivityAsync();
    }

    private async Task StopActivityAsync()
    {
        if (engine?.IsRunning != true || isStopping || keepAwake is null) return;
        isStopping = true;
        autoStopTimer.Stop();
        ToggleButton.IsEnabled = false;
        try
        {
            await engine.StopAsync();
            keepAwake.Disable();
            SetRunningVisuals(false);
            Icon = stopIcon;
            ToggleButton.Content = "Start";
            OptionsButton.IsEnabled = true;
            var status = GetIdleStatus();
            SetStatusText(status);
        }
        catch (Exception ex)
        {
            SetStatusText($"Could not stop activity: {ex.Message}");
        }
        finally
        {
            ToggleButton.IsEnabled = true;
            isStopping = false;
        }
    }

    private async void OnOptionsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new OptionsWindow(Volatile.Read(ref settings));
        var saved = await dialog.ShowDialog<bool>(this);
        if (!saved || dialog.SavedSettings is not { } updatedSettings) return;

        try
        {
            await settingsStore.SaveAsync(updatedSettings);
            var oldHotkey = settings.GlobalHotkey;
            Volatile.Write(ref settings, updatedSettings);
            if (!string.Equals(oldHotkey, updatedSettings.GlobalHotkey, StringComparison.OrdinalIgnoreCase))
                hotkeyWarning = TryConfigureGlobalHotkey(updatedSettings.GlobalHotkey) ?? string.Empty;
            if (engine?.IsRunning != true)
                SetStatusText(GetIdleStatus());
        }
        catch (Exception ex)
        {
            SetStatusText($"Could not save settings: {ex.Message}");
        }
    }

    private void SetStatusText(string message)
    {
        StatusText.Text = message;
        StatusText.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnGlobalHotkeyPressed() =>
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await ToggleActivityAsync();
            }
            catch (Exception ex)
            {
                SetStatusText($"Global hotkey action failed: {ex.Message}");
            }
        });

    private string? TryConfigureGlobalHotkey(string shortcut)
    {
        try
        {
            var replacement = PlatformServices.CreateGlobalHotkey(shortcut, OnGlobalHotkeyPressed);
            var previous = globalHotkey;
            globalHotkey = replacement;
            previous?.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return $"Global hotkey unavailable: {ex.Message}";
        }
    }

    private string GetCurrentWarning() =>
        string.Join(" ", new[] { sessionWarning, hotkeyWarning }
            .Where(message => !string.IsNullOrWhiteSpace(message)));

    private string GetIdleStatus()
    {
        var session = settings.ShowSessionInfo ? FormatSessionStats() : string.Empty;
        var warning = GetCurrentWarning();
        return string.Join(" ", new[] { session, warning }.Where(message => !string.IsNullOrWhiteSpace(message)));
    }

    private void SetRunningVisuals(bool isRunning)
    {
        RunningIndicator.Opacity = isRunning ? 1 : 0;
        RunningIndicator.IsHitTestVisible = isRunning;
        if (isRunning)
        {
            RunningLabel.Text = settings.ShowSessionInfo
                ? "Running · 00:00:00 · 0 cycles"
                : "Running";
            spinnerTimer.Start();
            sessionStatsTimer.Start();
            SetStatusText(string.Empty);
        }
        else
        {
            spinnerTimer.Stop();
            sessionStatsTimer.Stop();
            leftSpinnerRotation.Angle = 0;
            rightSpinnerRotation.Angle = 0;
            RunningLabel.Text = "Running";
        }
    }

    private void UpdateRunningStats()
    {
        if (engine is null) return;
        RunningLabel.Text = settings.ShowSessionInfo
            ? $"Running · {engine.SessionDuration:hh\\:mm\\:ss} · {engine.ActivityCount} cycles"
            : "Running";
    }

    private string FormatSessionStats() =>
        $"Last session: {engine?.SessionDuration:hh\\:mm\\:ss} · {engine?.ActivityCount ?? 0} cycles";

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        autoStopTimer.Stop();
        SetRunningVisuals(false);
        globalHotkey?.Dispose();
        engine?.StopAsync().GetAwaiter().GetResult();
        keepAwake?.Dispose();
        input?.Dispose();
    }
}
