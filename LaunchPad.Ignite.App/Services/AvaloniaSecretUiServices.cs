using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using LaunchPad.Ignite.App.Views;

namespace LaunchPad.Ignite.App.Services;

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

public sealed class AvaloniaClipboardService : IClipboardService
{
    private readonly Func<IClipboard?> _getClipboard;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private CancellationTokenSource? _expirationCancellation;

    public AvaloniaClipboardService(Func<IClipboard?> getClipboard, TimeProvider? timeProvider = null)
    {
        _getClipboard = getClipboard;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task CopyWithExpiryAsync(string value, TimeSpan expiry)
    {
        var clipboard = _getClipboard()
            ?? throw new InvalidOperationException("The system clipboard is not available.");
        await _operationLock.WaitAsync();
        try
        {
            _expirationCancellation?.Cancel();
            await clipboard.SetTextAsync(value);
            var cancellation = new CancellationTokenSource();
            _expirationCancellation = cancellation;
            _ = ExpireAsync(clipboard, value, expiry, cancellation);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task ExpireAsync(
        IClipboard clipboard,
        string copiedValue,
        TimeSpan expiry,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(expiry, _timeProvider, cancellation.Token);
            await _operationLock.WaitAsync(cancellation.Token);
            try
            {
                var transfer = await clipboard.TryGetDataAsync();
                string? currentText;
                if (transfer is null)
                {
                    currentText = null;
                }
                else
                {
                    try
                    {
                        currentText = await transfer.TryGetTextAsync();
                    }
                    finally
                    {
                        transfer.Dispose();
                    }
                }

                if (string.Equals(currentText, copiedValue, StringComparison.Ordinal))
                {
                    await clipboard.ClearAsync();
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Trace.TraceError("Failed to clear a timed clipboard entry.");
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
