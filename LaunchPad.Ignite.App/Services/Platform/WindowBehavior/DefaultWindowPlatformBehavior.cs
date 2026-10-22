using System;

namespace LaunchPad.Ignite.App.Services.Platform.WindowBehavior;

internal sealed class DefaultWindowPlatformBehavior : IWindowPlatformBehavior
{
    public bool SupportsGlobalKeyboardHook => false;

    public WindowDeactivationAction DeactivationAction => OperatingSystem.IsMacOS()
        ? WindowDeactivationAction.Minimize
        : WindowDeactivationAction.None;

    public IActivationHotkeyRegistration? TryRegisterActivationHotkey(string settingValue, System.Action activationRequested, out string? error)
    {
        error = null;
        return null;
    }
}
