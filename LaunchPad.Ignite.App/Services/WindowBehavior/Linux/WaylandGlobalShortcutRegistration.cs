using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Services.Hotkeys;

namespace LaunchPad.Ignite.App.Services.WindowBehavior.Linux;

internal sealed class WaylandGlobalShortcutRegistration : IActivationHotkeyRegistration
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _registrationTask;
    private int _disposed;

    public WaylandGlobalShortcutRegistration(string settingValue, Action activationRequested)
    {
        _registrationTask = Task.Run(() => RegisterAsync(settingValue, activationRequested, _cancellationTokenSource.Token));
    }

    private static async Task RegisterAsync(string settingValue, Action activationRequested, CancellationToken cancellationToken)
    {
        try
        {
            if (!GlobalActivationHotkey.TryParse(settingValue, out var hotkey))
            {
                Trace.TraceWarning($"Unable to register invalid Wayland activation hotkey '{settingValue}'.");
                return;
            }

            await WaylandGlobalShortcutsPortal.RunAsync(hotkey, activationRequested, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceWarning($"Unable to register the Wayland activation hotkey: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        _ = _registrationTask.ContinueWith(
            _ => _cancellationTokenSource.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}