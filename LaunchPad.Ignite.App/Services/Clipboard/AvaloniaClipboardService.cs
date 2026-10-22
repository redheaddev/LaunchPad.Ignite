using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.App.Services.Clipboard;

public sealed class AvaloniaClipboardService : IClipboardService
{
    private readonly Func<IClipboardAccess?> _getClipboard;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _expirationStateLock = new();
    private CancellationTokenSource? _expirationCancellation;
    private DateTimeOffset? _expirationTime;
    private TimeSpan _expirationDuration;

    public AvaloniaClipboardService(Func<IClipboard?> getClipboard, TimeProvider? timeProvider = null)
        : this(() => getClipboard() is { } clipboard ? new AvaloniaClipboardAccess(clipboard) : null, timeProvider)
    {
    }

    public AvaloniaClipboardService(Func<IClipboardAccess?> getClipboard, TimeProvider? timeProvider = null)
    {
        _getClipboard = getClipboard;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public TimeSpan ExpirationDuration
    {
        get
        {
            lock (_expirationStateLock)
            {
                return _expirationDuration;
            }
        }
    }

    public DateTimeOffset? ExpirationTime
    {
        get
        {
            lock (_expirationStateLock)
            {
                return _expirationTime;
            }
        }
    }

    public bool ExtendExpiry(TimeSpan extension)
    {
        if (extension <= TimeSpan.Zero)
        {
            return false;
        }

        lock (_expirationStateLock)
        {
            if (_expirationTime is not { } expirationTime || expirationTime <= _timeProvider.GetUtcNow())
            {
                return false;
            }

            _expirationTime = expirationTime + extension;
            _expirationDuration += extension;
            return true;
        }
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
            lock (_expirationStateLock)
            {
                _expirationCancellation = cancellation;
                _expirationTime = _timeProvider.GetUtcNow() + expiry;
                _expirationDuration = expiry;
            }

            _ = ExpireAsync(clipboard, value, cancellation);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task ExpireAsync(
        IClipboardAccess clipboard,
        string copiedValue,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (true)
            {
                DateTimeOffset expirationTime;
                lock (_expirationStateLock)
                {
                    expirationTime = _expirationTime ?? _timeProvider.GetUtcNow();
                }

                var remaining = expirationTime - _timeProvider.GetUtcNow();
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, _timeProvider, cancellation.Token);
                }

                await _operationLock.WaitAsync(cancellation.Token);
                try
                {
                    lock (_expirationStateLock)
                    {
                        if (_expirationTime is { } updatedExpirationTime
                            && updatedExpirationTime > _timeProvider.GetUtcNow())
                        {
                            continue;
                        }

                        if (ReferenceEquals(_expirationCancellation, cancellation))
                        {
                            _expirationTime = null;
                            _expirationDuration = TimeSpan.Zero;
                            _expirationCancellation = null;
                        }
                    }

                    var currentText = await clipboard.GetTextAsync();

                    if (string.Equals(currentText, copiedValue, StringComparison.Ordinal))
                    {
                        await clipboard.ClearAsync();
                    }

                    return;
                }
                finally
                {
                    _operationLock.Release();
                }
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

public interface IClipboardAccess
{
    Task SetTextAsync(string value);

    Task<string?> GetTextAsync();

    Task ClearAsync();
}

internal sealed class AvaloniaClipboardAccess : IClipboardAccess
{
    private readonly IClipboard _clipboard;

    public AvaloniaClipboardAccess(IClipboard clipboard)
    {
        _clipboard = clipboard;
    }

    public Task SetTextAsync(string value) => _clipboard.SetTextAsync(value);

    public Task<string?> GetTextAsync() => _clipboard.TryGetTextAsync();

    public Task ClearAsync() => _clipboard.ClearAsync();
}
