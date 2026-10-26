using LaunchPad.Ignite.App.Services.Hotkeys.Windows;

namespace LaunchPad.Ignite.App.Services.WindowBehavior.Windows;

internal sealed class WindowsWindowPlatformBehavior : IWindowPlatformBehavior
{
    public bool SupportsGlobalKeyboardHook => true;

    public bool SupportsSavedActivationHotkeyReset => false;

    public WindowDeactivationAction DeactivationAction => WindowDeactivationAction.Hide;

    public bool TryResetSavedActivationHotkey(out string? error)
    {
        error = null;
        return true;
    }

    public IActivationHotkeyRegistration? TryRegisterActivationHotkey(
        string settingValue,
        System.Action activationRequested,
        out string? error)
    {
        var hotkey = WindowsRegisteredHotkey.TryCreate(settingValue, activationRequested, out error);
        return hotkey is null ? null : new Registration(hotkey);
    }

    private sealed class Registration(WindowsRegisteredHotkey hotkey) : IActivationHotkeyRegistration
    {
        public void Dispose() => hotkey.Dispose();
    }
}
