using Avalonia;

namespace MMAway;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = new SingleInstance();
        if (!instance.IsPrimary)
        {
            try
            {
                instance.ActivatePrimaryAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or ArgumentException)
            {
                App.StartupError = $"Could not open the running MMAway window: {ex.Message}";
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            return;
        }

        App.Instance = instance;
        MMAway.Input.PlatformServices.Initialize();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
