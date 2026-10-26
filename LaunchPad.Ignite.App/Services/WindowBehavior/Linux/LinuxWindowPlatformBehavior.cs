using System;
using System.Diagnostics;
using System.Text;

namespace LaunchPad.Ignite.App.Services.WindowBehavior.Linux;

internal sealed class LinuxWindowPlatformBehavior : IWindowPlatformBehavior
{
    private const string GlobalShortcutsApplicationSchema =
        "org.gnome.settings-daemon.global-shortcuts.application:/org/gnome/settings-daemon/global-shortcuts/io.github.redheaddev.launchpadignite/";
    private readonly bool _isWaylandSession;
    private readonly bool _isGnomeSession;
    private readonly Func<string, System.Action, IActivationHotkeyRegistration> _waylandRegistrationFactory;
    private readonly Func<string?> _resetWaylandShortcut;

    public LinuxWindowPlatformBehavior()
        : this(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            static (settingValue, activationRequested) =>
                new WaylandGlobalShortcutRegistration(settingValue, activationRequested),
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            ResetGnomeSavedShortcut)
    {
    }

    internal LinuxWindowPlatformBehavior(
        string? sessionType,
        string? waylandDisplay,
        Func<string, System.Action, IActivationHotkeyRegistration>? waylandRegistrationFactory = null,
        string? currentDesktop = null,
        Func<string?>? resetWaylandShortcut = null)
    {
        _isWaylandSession = LinuxSessionDetector.IsWaylandSession(sessionType, waylandDisplay);
        _isGnomeSession = currentDesktop?.Contains("GNOME", StringComparison.OrdinalIgnoreCase) == true;
        _waylandRegistrationFactory = waylandRegistrationFactory
            ?? (static (settingValue, activationRequested) =>
                new WaylandGlobalShortcutRegistration(settingValue, activationRequested));
        _resetWaylandShortcut = resetWaylandShortcut ?? ResetGnomeSavedShortcut;
    }

    public bool SupportsGlobalKeyboardHook => !_isWaylandSession;

    public bool SupportsSavedActivationHotkeyReset => _isWaylandSession && _isGnomeSession;

    public WindowDeactivationAction DeactivationAction => WindowDeactivationAction.None;

    public bool TryResetSavedActivationHotkey(out string? error)
    {
        error = null;
        if (!SupportsSavedActivationHotkeyReset)
        {
            return true;
        }

        error = _resetWaylandShortcut();
        return error is null;
    }

    public IActivationHotkeyRegistration? TryRegisterActivationHotkey(
        string settingValue,
        System.Action activationRequested,
        out string? error)
    {
        error = null;
        return _isWaylandSession
            ? _waylandRegistrationFactory(settingValue, activationRequested)
            : null;
    }

    private static string? ResetGnomeSavedShortcut()
    {
        try
        {
            var startInfo = new ProcessStartInfo("gsettings")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("reset");
            startInfo.ArgumentList.Add(GlobalShortcutsApplicationSchema);
            startInfo.ArgumentList.Add("shortcuts");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "Could not start gsettings.";
            }

            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                return null;
            }

            var detail = standardError.Trim();
            return string.IsNullOrEmpty(detail)
                ? $"gsettings exited with code {process.ExitCode}."
                : detail;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return $"Could not run gsettings: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            return $"Could not reset the saved GNOME shortcut: {exception.Message}";
        }
    }
}