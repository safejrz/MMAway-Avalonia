using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace MMAway;

public class App : Application
{
    internal static SingleInstance? Instance { get; set; }
    internal static string? StartupError { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (StartupError is { } error)
            {
                var close = new Button { Content = "Close", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
                var errorWindow = new Window
                {
                    Title = "MMAway",
                    Width = 400,
                    SizeToContent = SizeToContent.Height,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Spacing = 16,
                        Children = { new TextBlock { Text = error, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, close }
                    }
                };
                close.Click += (_, _) => errorWindow.Close();
                desktop.MainWindow = errorWindow;
            }
            else
            {
                var window = new MainWindow();
                desktop.MainWindow = window;
                Instance?.StartListening(
                    () => Dispatcher.UIThread.Post(window.ActivateFromShortcut),
                    ex => Dispatcher.UIThread.Post(() => window.ShowActivationError(ex)));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
