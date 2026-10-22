namespace LaunchPad.Ignite.App.Services.WindowBehavior;

internal interface IWindowPlatformBehavior
{
    bool SupportsGlobalKeyboardHook { get; }

    WindowDeactivationAction DeactivationAction { get; }

    IActivationHotkeyRegistration? TryRegisterActivationHotkey(string settingValue, System.Action activationRequested, out string? error);
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
