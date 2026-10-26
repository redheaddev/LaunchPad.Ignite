using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
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
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string ShortcutId = "launchpad-activate";

    public static async Task RunAsync(
        GlobalActivationHotkey hotkey,
        Action activationRequested,
        ChannelReader<bool> configurationRequests,
        CancellationToken cancellationToken)
    {
        WaylandHotkeyDiagnostics.Write("Connecting to the desktop session bus.");
        var sessionBusAddress = DBusAddress.Session
            ?? throw new InvalidOperationException("The desktop session bus address is unavailable.");
        var connectionOptions = new DBusConnectionOptions(sessionBusAddress)
        {
            OnException = context => WaylandHotkeyDiagnostics.Write(
                $"D-Bus connection reported an exception from {context.Source}.",
                context.Exception)
        };
        var connection = new DBusConnection(connectionOptions);

        string? sessionHandle = null;
        var sessionHandleLock = new object();
        IDisposable? activationWatch = null;
        try
        {
            await connection.ConnectAsync();
            cancellationToken.ThrowIfCancellationRequested();
            WaylandHotkeyDiagnostics.Write($"Connected to the session bus as {connection.UniqueName}.");

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
                        WaylandHotkeyDiagnostics.Write("Received the registered global shortcut activation signal.");
                        activationRequested();
                    }

                    return ValueTask.CompletedTask;
                },
                ObserverFlags.None,
                emitOnCapturedContext: false);

            await VerifySessionBusMethodCallsAsync(connection);
            var createdSessionHandle = await CreateSessionAsync(connection, cancellationToken);
            lock (sessionHandleLock)
            {
                sessionHandle = createdSessionHandle;
            }

            WaylandHotkeyDiagnostics.Write("Desktop portal session created.");
            var bindResponse = await BindShortcutAsync(connection, createdSessionHandle, hotkey, cancellationToken);
            WaylandHotkeyDiagnostics.Write($"Desktop portal accepted shortcut binding (response code {bindResponse.Code}).");
            await ProcessConfigurationRequestsAsync(
                connection,
                createdSessionHandle,
                configurationRequests,
                cancellationToken);
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
                WaylandHotkeyDiagnostics.Write("Closing desktop portal session.");
                await CloseSessionAsync(connection, activeSessionHandle);
            }

            connection.Dispose();
            WaylandHotkeyDiagnostics.Write("Disconnected from the desktop session bus.");
        }
    }

    private static async Task<string> CreateSessionAsync(DBusConnection connection, CancellationToken cancellationToken)
    {
        var requestToken = $"request_{CreateHandleToken()}";
        var response = await SendRequestAsync(
            connection,
            "CreateSession",
            requestToken,
            "a{sv}",
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

        return GetSessionHandle(handle);
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

        WaylandHotkeyDiagnostics.Write($"Requesting portal shortcut '{ShortcutId}' with preferred trigger '{trigger}'.");
        var requestToken = $"request_{CreateHandleToken()}";
        return SendRequestAsync(
            connection,
            "BindShortcuts",
            requestToken,
            "oa(sa{sv})sa{sv}",
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
        string signature,
        MessageBodyWriter writeBody,
        CancellationToken cancellationToken)
    {
        var responseCompletion = new TaskCompletionSource<PortalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uniqueBusName = connection.UniqueName
            ?? throw new InvalidOperationException("The session bus did not assign a unique connection name.");
        using var responseWatch = await connection.WatchSignalAsync<PortalResponse>(
            Destination,
            GetRequestPath(uniqueBusName, requestToken),
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

        var message = CreateMethodCall(connection, Destination, ObjectPath, GlobalShortcutsInterface, method, signature, writeBody);
        WaylandHotkeyDiagnostics.Write(
            $"Calling desktop portal {Destination} {GlobalShortcutsInterface}.{method} with signature '{signature}' from {uniqueBusName}.");
        string requestHandle;
        try
        {
            requestHandle = await connection.CallMethodAsync<string>(message, ReadObjectPath);
            WaylandHotkeyDiagnostics.Write($"Desktop portal {method} method returned request handle {requestHandle}.");
        }
        catch (Exception exception)
        {
            WaylandHotkeyDiagnostics.Write($"D-Bus method call {method} did not return a request handle.", exception);
            throw;
        }

        var response = await responseCompletion.Task.WaitAsync(cancellationToken);
        WaylandHotkeyDiagnostics.Write($"Desktop portal {method} response code: {response.Code}.");
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
            var message = CreateMethodCall(connection, Destination, sessionHandle, "org.freedesktop.portal.Session", "Close", string.Empty);
            await connection.CallMethodAsync(message);
            WaylandHotkeyDiagnostics.Write("Desktop portal session closed.");
        }
        catch (Exception exception)
        {
            WaylandHotkeyDiagnostics.Write("Unable to close the desktop portal session.", exception);
        }
    }

    private static async Task ConfigureShortcutAsync(
        DBusConnection connection,
        string sessionHandle,
        CancellationToken cancellationToken)
    {
        var version = await GetGlobalShortcutsVersionAsync(connection);
        if (version < 2)
        {
            throw new NotSupportedException(
                $"The desktop Global Shortcuts portal version {version} does not support shortcut configuration.");
        }

        var message = CreateMethodCall(
            connection,
            Destination,
            ObjectPath,
            GlobalShortcutsInterface,
            "ConfigureShortcuts",
            "osa{sv}",
            (ref MessageWriter writer) =>
            {
                writer.WriteObjectPath(sessionHandle);
                writer.WriteString(string.Empty);
                writer.WriteDictionary(new Dictionary<string, VariantValue>());
            });
        WaylandHotkeyDiagnostics.Write("Opening the desktop portal shortcut configuration for the saved hotkey.");
        await connection.CallMethodAsync(message);
        cancellationToken.ThrowIfCancellationRequested();
        WaylandHotkeyDiagnostics.Write("Desktop portal shortcut configuration was opened.");
    }

    private static async Task ProcessConfigurationRequestsAsync(
        DBusConnection connection,
        string sessionHandle,
        ChannelReader<bool> configurationRequests,
        CancellationToken cancellationToken)
    {
        await foreach (var _ in configurationRequests.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ConfigureShortcutAsync(connection, sessionHandle, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                WaylandHotkeyDiagnostics.Write("Unable to open desktop portal shortcut configuration.", exception);
            }
        }
    }

    private static async Task<uint> GetGlobalShortcutsVersionAsync(DBusConnection connection)
    {
        var message = CreateMethodCall(
            connection,
            Destination,
            ObjectPath,
            PropertiesInterface,
            "Get",
            "ss",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(GlobalShortcutsInterface);
                writer.WriteString("version");
            });
        var version = await connection.CallMethodAsync<VariantValue>(message, ReadVariantValue);
        return version.GetUInt32();
    }

    private static async Task VerifySessionBusMethodCallsAsync(DBusConnection connection)
    {
        var message = CreateMethodCall(
            connection,
            "org.freedesktop.DBus",
            "/org/freedesktop/DBus",
            "org.freedesktop.DBus",
            "GetNameOwner",
            "s",
            (ref MessageWriter writer) => writer.WriteString(Destination));

        WaylandHotkeyDiagnostics.Write("Checking D-Bus method calls with org.freedesktop.DBus.GetNameOwner.");
        try
        {
            var owner = await connection.CallMethodAsync<string>(message, ReadString);
            WaylandHotkeyDiagnostics.Write($"D-Bus method-call check succeeded; portal owner is {owner}.");
        }
        catch (Exception exception)
        {
            WaylandHotkeyDiagnostics.Write("D-Bus method-call check failed.", exception);
            throw;
        }
    }

    private static MessageBuffer CreateMethodCall(
        DBusConnection connection,
        string destination,
        string path,
        string @interface,
        string method,
        string signature,
        MessageBodyWriter? writeBody = null)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination, path, @interface, method, signature, MessageFlags.None);
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

    private static string ReadString(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return reader.ReadString();
    }

    private static VariantValue ReadVariantValue(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return reader.ReadVariantValue();
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
        var sanitizedBusName = uniqueBusName.Replace(":", string.Empty, StringComparison.Ordinal).Replace('.', '_');
        return $"{ObjectPath}/request/{sanitizedBusName}/{requestToken}";
    }

    internal static string GetSessionHandle(VariantValue handle) =>
        handle.Type switch
        {
            VariantValueType.ObjectPath => handle.GetObjectPathAsString(),
            VariantValueType.String => handle.GetString(),
            _ => throw new InvalidOperationException($"The desktop portal returned a session handle with unexpected type '{handle.Type}'.")
        };

    private delegate void MessageBodyWriter(ref MessageWriter writer);

    private sealed record PortalResponse(uint Code, Dictionary<string, VariantValue> Results);

    private sealed record PortalActivation(string SessionHandle, string ShortcutId);
}