using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LaunchPad.Ignite.App.Views;

namespace LaunchPad.Ignite.App.Services.Secrets;

public sealed class AvaloniaSecretEntryDialogService : ISecretEntryDialogService
{
    public Task<string?> ShowAsync(string title, bool requireConfirmation)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
        {
            return Task.FromResult<string?>(null);
        }

        var dialog = new SecretEntryWindow(title, requireConfirmation);
        return owner is MainWindow mainWindow
            ? mainWindow.ShowSecretEntryDialogAsync(dialog)
            : dialog.ShowDialog<string?>(owner);
    }

    public async Task ShowSecretAsync(string title, string value)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } owner)
        {
            return;
        }

        var dialog = new SecretEntryWindow(title, requireConfirmation: false, displayedValue: value);
        if (owner is MainWindow mainWindow)
        {
            await mainWindow.ShowSecretEntryDialogAsync(dialog);
        }
        else
        {
            await dialog.ShowDialog<string?>(owner);
        }
    }
}
