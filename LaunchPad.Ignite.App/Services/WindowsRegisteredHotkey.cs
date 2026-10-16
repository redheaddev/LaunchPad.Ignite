using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using SharpHook.Data;

namespace LaunchPad.Ignite.App.Services;

internal sealed class WindowsRegisteredHotkey : IDisposable
{
    private const int HotkeyId = 0x4C50;
    private const uint HotkeyMessage = 0x0312;
    private const uint QuitMessage = 0x0012;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWindows = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint PeekNoRemove = 0x0000;

    private readonly Action _activationRequested;
    private readonly uint _modifiers;
    private readonly uint _virtualKey;
    private readonly ManualResetEventSlim _registrationReady = new();
    private readonly Thread _messageThread;
    private uint _threadId;
    private bool _registered;
    private int _registrationError;
    private bool _disposed;

    private WindowsRegisteredHotkey(uint modifiers, uint virtualKey, Action activationRequested)
    {
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        _activationRequested = activationRequested;
        _messageThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "LaunchPad Windows activation hotkey"
        };
    }

    public static WindowsRegisteredHotkey? TryCreate(string settingValue, Action activationRequested, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!GlobalActivationHotkey.TryParse(settingValue, out var hotkey)
            || !TryGetVirtualKey(hotkey.Key, out var virtualKey))
        {
            error = $"The activation hotkey '{settingValue}' cannot be registered with Windows.";
            return null;
        }

        var modifiers = ModNoRepeat;
        if (hotkey.Alt) modifiers |= ModAlt;
        if (hotkey.Control) modifiers |= ModControl;
        if (hotkey.Shift) modifiers |= ModShift;
        if (hotkey.Meta) modifiers |= ModWindows;

        var listener = new WindowsRegisteredHotkey(modifiers, virtualKey, activationRequested);
        listener._messageThread.Start();
        listener._registrationReady.Wait();
        if (listener._registered)
        {
            return listener;
        }

        error = $"Windows rejected activation hotkey '{hotkey.DisplayName}' (error {listener._registrationError}).";
        listener.Dispose();
        return null;
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PeekNoRemove);
        _registered = RegisterHotKey(IntPtr.Zero, HotkeyId, _modifiers, _virtualKey);
        if (!_registered)
        {
            _registrationError = Marshal.GetLastWin32Error();
        }

        _registrationReady.Set();
        if (!_registered)
        {
            return;
        }

        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == HotkeyMessage && message.WParam.ToUInt64() == HotkeyId)
                {
                    _activationRequested();
                }
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Windows activation hotkey listener failed: {exception.Message}");
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    private static bool TryGetVirtualKey(KeyCode keyCode, out uint virtualKey)
    {
        var name = keyCode.ToString();
        if (name.StartsWith("VcNumPad", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan("VcNumPad".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var numpad)
            && numpad is >= 0 and <= 9)
        {
            virtualKey = 0x60u + (uint)numpad;
            return true;
        }

        if (name.StartsWith("VcF", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            virtualKey = 0x70u + (uint)(functionKey - 1);
            return true;
        }

        if (name.Length == 3
            && name.StartsWith("Vc", StringComparison.Ordinal)
            && char.IsAsciiLetterOrDigit(name[2]))
        {
            virtualKey = char.ToUpperInvariant(name[2]);
            return true;
        }

        virtualKey = name switch
        {
            "VcSpace" => 0x20,
            "VcEnter" => 0x0D,
            "VcTab" => 0x09,
            "VcEscape" => 0x1B,
            "VcBackspace" => 0x08,
            "VcInsert" => 0x2D,
            "VcDelete" => 0x2E,
            "VcHome" => 0x24,
            "VcEnd" => 0x23,
            "VcPageUp" => 0x21,
            "VcPageDown" => 0x22,
            "VcLeft" => 0x25,
            "VcUp" => 0x26,
            "VcRight" => 0x27,
            "VcDown" => 0x28,
            "VcMinus" => 0xBD,
            "VcEquals" => 0xBB,
            "VcOpenBracket" => 0xDB,
            "VcCloseBracket" => 0xDD,
            "VcBackslash" => 0xDC,
            "VcSemicolon" => 0xBA,
            "VcQuote" => 0xDE,
            "VcComma" => 0xBC,
            "VcPeriod" => 0xBE,
            "VcSlash" => 0xBF,
            _ => 0
        };

        return virtualKey != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registered && _threadId != 0)
        {
            _ = PostThreadMessage(_threadId, QuitMessage, IntPtr.Zero, IntPtr.Zero);
        }

        if (_messageThread.IsAlive && Thread.CurrentThread != _messageThread)
        {
            _messageThread.Join(TimeSpan.FromSeconds(1));
        }

        _registrationReady.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, IntPtr windowHandle, uint minimumMessage, uint maximumMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr windowHandle, uint minimumMessage, uint maximumMessage, uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Position;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
