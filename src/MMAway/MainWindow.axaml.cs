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
        PositionChanged += (_, e) => windowPosition = e.Point;
    }

    // Cached so the engine thread never has to block on the UI thread.
    private PixelPoint windowPosition;

    private static WindowIcon LoadIcon(string name) =>
        new(Avalonia.Platform.AssetLoader.Open(new Uri($"avares://MMAway/Assets/{name}")));

    private void OnOpened(object? sender, EventArgs e)
    {
        var bounds = (Screens.Primary ?? Screens.All.FirstOrDefault())?.Bounds ?? new PixelRect(0, 0, 1024, 768);
        Position = new PixelPoint(bounds.X + bounds.Width / 10, bounds.Y + bounds.Height / 5);
        var home = new PixelPoint(bounds.X + bounds.Width / 4, bounds.Y + bounds.Height / 5 * 4);

        try
        {
            input = PlatformServices.CreateInput();
            keepAwake = PlatformServices.CreateKeepAwake();
            engine = new ActivityEngine(
                input,
                home,
                () => windowPosition,
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
