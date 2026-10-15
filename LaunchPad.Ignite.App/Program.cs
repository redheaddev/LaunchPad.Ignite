using Avalonia;
using System;

namespace LaunchPad.Ignite.App;

sealed class Program
{
    private static SingleInstanceCoordinator? _instanceCoordinator;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        _instanceCoordinator = SingleInstanceCoordinator.AcquireOrReplaceExisting();
        if (_instanceCoordinator is null)
        {
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _instanceCoordinator.Dispose();
            _instanceCoordinator = null;
        }
    }

    internal static void StartListeningForReplacement(Action shutdownRequested)
    {
        _instanceCoordinator?.StartListening(shutdownRequested);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
