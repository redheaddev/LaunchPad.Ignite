namespace LaunchPad.Ignite.App.Services.WindowBehavior;

internal interface IWindowPlatformBehavior
{
    bool SupportsGlobalKeyboardHook { get; }

    bool SupportsSavedActivationHotkeyReset { get; }

    WindowDeactivationAction DeactivationAction { get; }

    bool TryResetSavedActivationHotkey(out string? error);

    IActivationHotkeyRegistration? TryRegisterActivationHotkey(
        string settingValue,
        System.Action activationRequested,
        out string? error);
}

internal enum WindowDeactivationAction
{
    None,
    Hide,
    Minimize
}

internal interface IActivationHotkeyRegistration : System.IDisposable
{
}

internal interface IConfigurableActivationHotkeyRegistration
{
    void RequestConfiguration();
}
