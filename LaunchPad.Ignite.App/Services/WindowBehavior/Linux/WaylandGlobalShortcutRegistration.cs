using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Services.Hotkeys;

namespace LaunchPad.Ignite.App.Services.WindowBehavior.Linux;

internal sealed class WaylandGlobalShortcutRegistration : IActivationHotkeyRegistration, IConfigurableActivationHotkeyRegistration
{
    private static readonly SemaphoreSlim RegistrationGate = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Channel<bool> _configurationRequests = Channel.CreateUnbounded<bool>();
    private readonly Task _registrationTask;
    private int _disposed;

    public WaylandGlobalShortcutRegistration(string settingValue, Action activationRequested)
    {
        _registrationTask = Task.Run(() => RegisterAsync(
            settingValue,
            activationRequested,
            _configurationRequests.Reader,
            _cancellationTokenSource.Token));
    }

    private static async Task RegisterAsync(
        string settingValue,
        Action activationRequested,
        ChannelReader<bool> configurationRequests,
        CancellationToken cancellationToken)
    {
        var gateAcquired = false;
        try
        {
            await RegistrationGate.WaitAsync(cancellationToken);
            gateAcquired = true;
            cancellationToken.ThrowIfCancellationRequested();

            if (!GlobalActivationHotkey.TryParse(settingValue, out var hotkey))
            {
                WaylandHotkeyDiagnostics.Write($"Registration skipped because '{settingValue}' is not a valid global shortcut.");
                Trace.TraceWarning($"Unable to register invalid Wayland activation hotkey '{settingValue}'.");
                return;
            }

            WaylandHotkeyDiagnostics.Write($"Starting registration for {hotkey.DisplayName}.");
            await WaylandGlobalShortcutsPortal.RunAsync(hotkey, activationRequested, configurationRequests, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WaylandHotkeyDiagnostics.Write("Registration was canceled because the setting changed or the app is closing.");
        }
        catch (Exception exception)
        {
            WaylandHotkeyDiagnostics.Write("Wayland activation shortcut registration failed.", exception);
            Trace.TraceWarning($"Unable to register the Wayland activation hotkey: {exception.Message}");
        }
        finally
        {
            if (gateAcquired)
            {
                RegistrationGate.Release();
            }
        }
    }

    public void RequestConfiguration()
    {
        if (_configurationRequests.Writer.TryWrite(true))
        {
            WaylandHotkeyDiagnostics.Write("Queued a request to configure the registered global shortcut.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        _configurationRequests.Writer.TryComplete();
        _ = _registrationTask.ContinueWith(
            _ => _cancellationTokenSource.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}