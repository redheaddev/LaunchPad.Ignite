using System;

namespace LaunchPad.Ignite.App.Services.WindowBehavior.Linux;

internal sealed class LinuxWindowPlatformBehavior : IWindowPlatformBehavior
{
    private readonly bool _isWaylandSession;
    private readonly Func<string, System.Action, IActivationHotkeyRegistration> _waylandRegistrationFactory;

    public LinuxWindowPlatformBehavior()
        : this(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            static (settingValue, activationRequested) => new WaylandGlobalShortcutRegistration(settingValue, activationRequested))
    {
    }

    internal LinuxWindowPlatformBehavior(
        string? sessionType,
        string? waylandDisplay,
        Func<string, System.Action, IActivationHotkeyRegistration>? waylandRegistrationFactory = null)
    {
        _isWaylandSession = LinuxSessionDetector.IsWaylandSession(sessionType, waylandDisplay);
        _waylandRegistrationFactory = waylandRegistrationFactory
            ?? (static (settingValue, activationRequested) => new WaylandGlobalShortcutRegistration(settingValue, activationRequested));
    }

    public bool SupportsGlobalKeyboardHook => !_isWaylandSession;

    public WindowDeactivationAction DeactivationAction => WindowDeactivationAction.None;

    public IActivationHotkeyRegistration? TryRegisterActivationHotkey(string settingValue, System.Action activationRequested, out string? error)
    {
        error = null;
        return _isWaylandSession
            ? _waylandRegistrationFactory(settingValue, activationRequested)
            : null;
    }
}