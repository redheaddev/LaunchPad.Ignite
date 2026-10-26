using System;

namespace LaunchPad.Ignite.App.Services.WindowBehavior;

internal sealed class DefaultWindowPlatformBehavior : IWindowPlatformBehavior
{
    public bool SupportsGlobalKeyboardHook => false;

    public bool SupportsSavedActivationHotkeyReset => false;

    public WindowDeactivationAction DeactivationAction => OperatingSystem.IsMacOS()
        ? WindowDeactivationAction.Minimize
        : WindowDeactivationAction.None;

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
        error = null;
        return null;
    }
}
