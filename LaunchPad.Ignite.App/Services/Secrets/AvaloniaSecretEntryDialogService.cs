using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LaunchPad.Ignite.App.Services.Clipboard;
using LaunchPad.Ignite.App.Views;

namespace LaunchPad.Ignite.App.Services.Secrets;

public sealed class AvaloniaSecretEntryDialogService : ISecretEntryDialogService
{
    private static readonly TimeSpan SecretClipboardExpiry = TimeSpan.FromSeconds(15);
    private readonly IClipboardService _clipboardService;

    public AvaloniaSecretEntryDialogService(IClipboardService? clipboardService = null)
    {
        _clipboardService = clipboardService ?? new AvaloniaClipboardService(GetSystemClipboard);
    }

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

        var dialog = new SecretRevealWindow(title, value, async () =>
            await _clipboardService.CopyWithExpiryAsync(value, SecretClipboardExpiry));
        if (owner is MainWindow mainWindow)
        {
            await mainWindow.ShowSecretRevealDialogAsync(dialog);
        }
        else
        {
            await dialog.ShowDialog<string?>(owner);
        }
    }

    private static Avalonia.Input.Platform.IClipboard? GetSystemClipboard()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard
            : null;
}
