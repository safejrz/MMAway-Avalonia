using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        PositionChanged += (_, _) => UpdateWindowBounds();
        Resized += (_, _) => UpdateWindowBounds();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateWindowBounds();
        };
    }

    // The engine thread must not touch Avalonia properties, so the effective bounds are
    // resolved on the UI thread and published as a single immutable reference.
    private sealed record BoundsSnapshot(PixelRect Rect);

    private BoundsSnapshot currentBounds = new(new PixelRect(0, 0, 1024, 768));
    private PixelRect screenBounds = new(0, 0, 1024, 768);

    // Falls back to the screen when the window is minimized or not measured yet.
    private void UpdateWindowBounds()
    {
        var origin = this.PointToScreen(new Point(0, 0));
        var scale = RenderScaling;
        var rect = new PixelRect(origin.X, origin.Y,
            (int)(ClientSize.Width * scale), (int)(ClientSize.Height * scale));
        var useScreen = WindowState == WindowState.Minimized || rect.Width <= 0 || rect.Height <= 0;
        Volatile.Write(ref currentBounds, new BoundsSnapshot(useScreen ? screenBounds : rect));
    }

    private PixelRect GetBounds() => Volatile.Read(ref currentBounds).Rect;

    private static WindowIcon LoadIcon(string name) =>
        new(Avalonia.Platform.AssetLoader.Open(new Uri($"avares://MMAway/Assets/{name}")));

    private void OnOpened(object? sender, EventArgs e)
    {
        var bounds = (Screens.Primary ?? Screens.All.FirstOrDefault())?.Bounds ?? new PixelRect(0, 0, 1024, 768);
        Position = new PixelPoint(bounds.X + bounds.Width / 10, bounds.Y + bounds.Height / 5);
        screenBounds = bounds;
        UpdateWindowBounds();

        try
        {
            input = PlatformServices.CreateInput();
            keepAwake = PlatformServices.CreateKeepAwake();
            engine = new ActivityEngine(
                input,
                GetBounds,
                text => Dispatcher.UIThread.Post(() => OutputBox.Text += text),
                () => Dispatcher.UIThread.Post(() => OutputBox.Clear()));
            StatusText.Text = PlatformServices.GetSessionWarning();
        }
        catch (Exception ex)
        {
            ToggleButton.IsEnabled = false;
            StatusText.Text = ex.Message;
        }
    }

    private async void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (engine is null || keepAwake is null || input is null) return;

        if (engine.IsRunning)
        {
            ToggleButton.IsEnabled = false;
            await engine.StopAsync();
            keepAwake.Disable();
            Icon = stopIcon;
            ToggleButton.Content = "Start";
            ToggleButton.IsEnabled = true;
            return;
        }

        keepAwake.Enable();
        Icon = runIcon;
        ToggleButton.Content = "Stop!";
        engine.Start();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        engine?.StopAsync().GetAwaiter().GetResult();
        keepAwake?.Dispose();
        input?.Dispose();
    }
}
