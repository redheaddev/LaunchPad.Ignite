using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    private readonly FileStream _ownershipLock;
    private readonly string _pipeName;
    private CancellationTokenSource? _listenerCancellation;
    private Task? _listenerTask;
    private int _disposed;

    private SingleInstanceCoordinator(FileStream ownershipLock, string pipeName)
    {
        _ownershipLock = ownershipLock;
        _pipeName = pipeName;
    }

    public static SingleInstanceCoordinator? AcquireOrReplaceExisting()
    {
        var identity = Environment.UserName + "|" + Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchPad.Ignite");
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, "instance.lock");
        var pipeName = $"LaunchPad.Ignite.{suffix}";
        return AcquireOrReplaceExisting(lockPath, pipeName, StartupTimeout);
    }

    internal static SingleInstanceCoordinator? AcquireOrReplaceExisting(string lockPath, string pipeName, TimeSpan startupTimeout)
    {
        var deadline = DateTime.UtcNow + startupTimeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var ownershipLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new SingleInstanceCoordinator(ownershipLock, pipeName);
            }
            catch (IOException)
            {
                RequestExistingInstanceShutdown(pipeName);
                Thread.Sleep(100);
            }
        }

        return null;
    }

    public void StartListening(Action shutdownRequested)
    {
        if (_listenerTask is not null)
        {
            throw new InvalidOperationException("The single-instance listener is already running.");
        }

        _listenerCancellation = new CancellationTokenSource();
        _listenerTask = ListenForShutdownAsync(shutdownRequested, _listenerCancellation.Token);
    }

    private async Task ListenForShutdownAsync(Action shutdownRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var shouldShutdown = false;
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var request = new byte[1];
                var bytesRead = await server.ReadAsync(request, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 1 && request[0] == 1)
                {
                    await server.WriteAsync(new byte[] { 1 }, cancellationToken).ConfigureAwait(false);
                    shouldShutdown = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            if (shouldShutdown)
            {
                shutdownRequested();
                return;
            }
        }
    }

    private static void RequestExistingInstanceShutdown(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect(100);
            client.WriteByte(1);
            client.Flush();
            if (client.ReadByte() != 1)
            {
                Thread.Sleep(100);
            }
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _listenerCancellation?.Cancel();
        _ownershipLock.Dispose();
        _listenerCancellation?.Dispose();
    }
}
