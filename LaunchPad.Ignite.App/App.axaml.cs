using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LaunchPad.Ignite.App.ViewModels;
using LaunchPad.Ignite.App.Views;
using System;
using Avalonia.Threading;

namespace LaunchPad.Ignite.App;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktopLifetime = desktop;
            _mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
            desktop.MainWindow = _mainWindow;
            desktop.Exit += Desktop_Exit;
            Program.StartListeningForReplacement(() => Dispatcher.UIThread.Post(() => desktop.Shutdown()));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow_Click(object? sender, EventArgs e)
    {
        _mainWindow?.ShowAndActivate();
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        _desktopLifetime?.Shutdown();
    }

    private void Desktop_Exit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_mainWindow?.DataContext is MainViewModel viewModel)
        {
            viewModel.ShutdownExtensions();
        }
    }
}