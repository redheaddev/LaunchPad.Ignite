using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Services.Hotkeys;
using Tmds.DBus.Protocol;

namespace LaunchPad.Ignite.App.Services.WindowBehavior.Linux;

internal static class WaylandGlobalShortcutsPortal
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string GlobalShortcutsInterface = "org.freedesktop.portal.GlobalShortcuts";
    private const string RequestInterface = "org.freedesktop.portal.Request";
    private const string ShortcutId = "launchpad-activate";

    public static async Task RunAsync(
        GlobalActivationHotkey hotkey,
        Action activationRequested,
        CancellationToken cancellationToken)
    {
        var connection = new DBusConnection(DBusAddress.Session);
        await connection.ConnectAsync();
        cancellationToken.ThrowIfCancellationRequested();

        string? sessionHandle = null;
        var sessionHandleLock = new object();
        IDisposable? activationWatch = null;
        try
        {
            activationWatch = await connection.WatchSignalAsync<PortalActivation>(
                Destination,
                null!,
                GlobalShortcutsInterface,
                "Activated",
                ReadActivation,
                notification =>
                {
                    string? activeSessionHandle;
                    lock (sessionHandleLock)
                    {
                        activeSessionHandle = sessionHandle;
                    }

                    if (notification.HasValue
                        && !cancellationToken.IsCancellationRequested
                        && string.Equals(notification.Value.SessionHandle, activeSessionHandle, StringComparison.Ordinal)
                        && string.Equals(notification.Value.ShortcutId, ShortcutId, StringComparison.Ordinal))
                    {
                        activationRequested();
                    }

                    return ValueTask.CompletedTask;
                },
                ObserverFlags.None,
                emitOnCapturedContext: false);

            var createdSessionHandle = await CreateSessionAsync(connection, cancellationToken);
            lock (sessionHandleLock)
            {
                sessionHandle = createdSessionHandle;
            }

            await BindShortcutAsync(connection, sessionHandle, hotkey, cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            activationWatch?.Dispose();
            string? activeSessionHandle;
            lock (sessionHandleLock)
            {
                activeSessionHandle = sessionHandle;
            }

            if (activeSessionHandle is not null)
            {
                await CloseSessionAsync(connection, activeSessionHandle);
            }

            connection.Dispose();
        }
    }

    private static async Task<string> CreateSessionAsync(DBusConnection connection, CancellationToken cancellationToken)
    {
        var requestToken = $"request_{CreateHandleToken()}";
        var response = await SendRequestAsync(
            connection,
            "CreateSession",
            requestToken,
            (ref MessageWriter writer) => writer.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = VariantValue.String(requestToken),
                ["session_handle_token"] = VariantValue.String($"session_{CreateHandleToken()}")
            }),
            cancellationToken);

        if (!response.Results.TryGetValue("session_handle", out var handle))
        {
            throw new InvalidOperationException("The desktop portal did not return a session handle.");
        }

        return handle.GetObjectPathAsString();
    }

    private static Task<PortalResponse> BindShortcutAsync(
        DBusConnection connection,
        string sessionHandle,
        GlobalActivationHotkey hotkey,
        CancellationToken cancellationToken)
    {
        if (!WaylandShortcutTrigger.TryFormat(hotkey, out var trigger))
        {
            throw new InvalidOperationException($"The activation key '{hotkey.Key}' cannot be represented by the desktop portal.");
        }

        var requestToken = $"request_{CreateHandleToken()}";
        return SendRequestAsync(
            connection,
            "BindShortcuts",
            requestToken,
            (ref MessageWriter writer) =>
            {
                writer.WriteObjectPath(sessionHandle);
                var shortcuts = writer.WriteArrayStart(DBusType.Struct);
                writer.WriteStructureStart();
                writer.WriteString(ShortcutId);
                writer.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["description"] = VariantValue.String("Activate LaunchPad"),
                    ["preferred_trigger"] = VariantValue.String(trigger)
                });
                writer.WriteArrayEnd(shortcuts);
                writer.WriteString(string.Empty);
                writer.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["handle_token"] = VariantValue.String(requestToken)
                });
            },
            cancellationToken);
    }

    private static async Task<PortalResponse> SendRequestAsync(
        DBusConnection connection,
        string method,
        string requestToken,
        MessageBodyWriter writeBody,
        CancellationToken cancellationToken)
    {
        var responseCompletion = new TaskCompletionSource<PortalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var responseWatch = await connection.WatchSignalAsync<PortalResponse>(
            Destination,
            GetRequestPath(connection.UniqueName, requestToken),
            RequestInterface,
            "Response",
            ReadResponse,
            notification =>
            {
                if (notification.HasValue)
                {
                    responseCompletion.TrySetResult(notification.Value);
                }

                return ValueTask.CompletedTask;
            },
            ObserverFlags.None,
            emitOnCapturedContext: false);

        var message = CreateMethodCall(connection, ObjectPath, GlobalShortcutsInterface, method, writeBody);
        _ = await connection.CallMethodAsync<string>(message, ReadObjectPath);

        var response = await responseCompletion.Task.WaitAsync(cancellationToken);
        if (response.Code != 0)
        {
            throw new InvalidOperationException(response.Code == 1
                ? "The desktop portal cancelled global shortcut registration."
                : "The desktop portal failed to register the global shortcut.");
        }

        return response;
    }

    private static async Task CloseSessionAsync(DBusConnection connection, string sessionHandle)
    {
        try
        {
            var message = CreateMethodCall(connection, sessionHandle, "org.freedesktop.portal.Session", "Close");
            await connection.CallMethodAsync(message);
        }
        catch
        {
        }
    }

    private static MessageBuffer CreateMethodCall(
        DBusConnection connection,
        string path,
        string @interface,
        string method,
        MessageBodyWriter? writeBody = null)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(Destination, path, @interface, method);
            writeBody?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static string ReadObjectPath(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return reader.ReadObjectPathAsString();
    }

    private static PortalResponse ReadResponse(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return new PortalResponse(reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
    }

    private static PortalActivation ReadActivation(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        var sessionHandle = reader.ReadObjectPathAsString();
        var shortcutId = reader.ReadString();
        _ = reader.ReadUInt64();
        _ = reader.ReadDictionaryOfStringToVariantValue();
        return new PortalActivation(sessionHandle, shortcutId);
    }

    private static string CreateHandleToken() => $"launchpad_{Guid.NewGuid():N}";

    internal static string GetRequestPath(string uniqueBusName, string requestToken)
    {
        var sanitizedBusName = uniqueBusName.Replace(':', '_').Replace('.', '_');
        return $"{ObjectPath}/request/{sanitizedBusName}/{requestToken}";
    }

    private delegate void MessageBodyWriter(ref MessageWriter writer);

    private sealed record PortalResponse(uint Code, Dictionary<string, VariantValue> Results);

    private sealed record PortalActivation(string SessionHandle, string ShortcutId);
}