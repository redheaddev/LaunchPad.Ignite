using LaunchPad.Ignite.App.Services.Platform.Hotkeys.Windows;

namespace LaunchPad.Ignite.App.Services.Platform.WindowBehavior.Windows;

internal sealed class WindowsWindowPlatformBehavior : IWindowPlatformBehavior
{
    public bool SupportsGlobalKeyboardHook => true;

    public WindowDeactivationAction DeactivationAction => WindowDeactivationAction.Hide;

    public IActivationHotkeyRegistration? TryRegisterActivationHotkey(string settingValue, System.Action activationRequested, out string? error)
    {
        var hotkey = WindowsRegisteredHotkey.TryCreate(settingValue, activationRequested, out error);
        return hotkey is null ? null : new Registration(hotkey);
    }

    private sealed class Registration(WindowsRegisteredHotkey hotkey) : IActivationHotkeyRegistration
    {
        public void Dispose() => hotkey.Dispose();
    }
}
