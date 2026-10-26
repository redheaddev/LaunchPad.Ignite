using LaunchPad.Ignite.App.Services.WindowBehavior;
using LaunchPad.Ignite.App.Services.WindowBehavior.Linux;
using LaunchPad.Ignite.App.Services.Hotkeys;
using System.IO;
using Tmds.DBus.Protocol;

namespace LaunchPad.Ignite.Tests;

public class LinuxWindowPlatformBehaviorTests
{
    [Fact]
    public void SupportsGlobalKeyboardHook()
    {
        var platform = new LinuxWindowPlatformBehavior("x11", null);

        Assert.True(platform.SupportsGlobalKeyboardHook);
    }

    [Fact]
    public void UsesKeyboardHookFallbackInsteadOfNativeRegistration()
    {
        var platform = new LinuxWindowPlatformBehavior("x11", null);

        var registration = platform.TryRegisterActivationHotkey("Alt+Space", () => { }, out var error);

        Assert.Null(registration);
        Assert.Null(error);
    }

    [Fact]
    public void WaylandUsesPortalRegistrationWithoutStartingKeyboardHook()
    {
        var expectedRegistration = new TestRegistration();
        var configurationRequested = false;
        var platform = new LinuxWindowPlatformBehavior(
            "wayland",
            null,
            (_, _) =>
            {
                configurationRequested = true;
                return expectedRegistration;
            });

        var registration = platform.TryRegisterActivationHotkey("Alt+Space", () => { }, out var error);

        Assert.False(platform.SupportsGlobalKeyboardHook);
        Assert.Same(expectedRegistration, registration);
        Assert.True(configurationRequested);
        Assert.Null(error);
    }

    [Fact]
    public void ResetsSavedShortcutOnlyForWaylandSessions()
    {
        var resetCount = 0;
        var wayland = new LinuxWindowPlatformBehavior(
            "wayland",
            null,
            currentDesktop: "ubuntu:GNOME",
            resetWaylandShortcut: () =>
            {
                resetCount++;
                return null;
            });
        var x11 = new LinuxWindowPlatformBehavior(
            "x11",
            null,
            resetWaylandShortcut: () =>
            {
                resetCount++;
                return null;
            });

        Assert.True(wayland.TryResetSavedActivationHotkey(out var waylandError));
        Assert.Null(waylandError);
        Assert.True(x11.TryResetSavedActivationHotkey(out var x11Error));
        Assert.Null(x11Error);
        Assert.Equal(1, resetCount);
    }

    [Fact]
    public void ReportsSavedShortcutResetFailure()
    {
        const string expectedError = "gsettings schema unavailable";
        var platform = new LinuxWindowPlatformBehavior(
            "wayland",
            null,
            currentDesktop: "ubuntu:GNOME",
            resetWaylandShortcut: () => expectedError);

        Assert.False(platform.TryResetSavedActivationHotkey(out var error));
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void DoesNotAttemptGnomeShortcutResetOutsideGnomeWayland()
    {
        var resetCount = 0;
        var platform = new LinuxWindowPlatformBehavior(
            "wayland",
            null,
            currentDesktop: "KDE",
            resetWaylandShortcut: () =>
            {
                resetCount++;
                return null;
            });

        Assert.False(platform.SupportsSavedActivationHotkeyReset);
        Assert.True(platform.TryResetSavedActivationHotkey(out var error));
        Assert.Null(error);
        Assert.Equal(0, resetCount);
    }

    [Theory]
    [InlineData("wayland", null, true)]
    [InlineData("x11", ":0", false)]
    [InlineData(null, "wayland-0", true)]
    [InlineData(null, null, false)]
    public void DetectsWaylandSession(string? sessionType, string? waylandDisplay, bool expected)
    {
        Assert.Equal(expected, LinuxSessionDetector.IsWaylandSession(sessionType, waylandDisplay));
    }

    [Fact]
    public void BuildsPortalRequestPathFromUniqueBusNameAndToken()
    {
        var requestPath = WaylandGlobalShortcutsPortal.GetRequestPath(":1.42", "request_launchpad_test");

        Assert.Equal(
            "/org/freedesktop/portal/desktop/request/1_42/request_launchpad_test",
            requestPath);
    }

    [Fact]
    public void ReadsStringSessionHandleReturnedByPortal()
    {
        var sessionHandle = WaylandGlobalShortcutsPortal.GetSessionHandle(VariantValue.String(
            "/org/freedesktop/portal/desktop/session/1_42/session_launchpad_test"));

        Assert.Equal(
            "/org/freedesktop/portal/desktop/session/1_42/session_launchpad_test",
            sessionHandle);
    }

    [Fact]
    public void UsesXdgStateHomeForWaylandDiagnostics()
    {
        var stateHome = Path.Combine(Path.GetTempPath(), "launchpad-state");

        var path = WaylandHotkeyDiagnostics.GetLogPath(stateHome, Path.GetTempPath());

        Assert.Equal(Path.Combine(stateHome, "LaunchPad.Ignite", "activation-hotkey.log"), path);
    }

    [Fact]
    public void DefaultsWaylandDiagnosticsToLocalStateDirectory()
    {
        var home = Path.Combine(Path.GetTempPath(), "launchpad-home");

        var path = WaylandHotkeyDiagnostics.GetLogPath(null, home);

        Assert.Equal(Path.Combine(home, ".local", "state", "LaunchPad.Ignite", "activation-hotkey.log"), path);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Space", "<Control><Alt>space")]
    [InlineData("Win+Shift+OemPlus", "<Shift><Super>equal")]
    [InlineData("Ctrl+F12", "<Control>F12")]
    public void FormatsPortalShortcutTriggers(string settingValue, string expected)
    {
        Assert.True(GlobalActivationHotkey.TryParse(settingValue, out var hotkey));

        Assert.True(WaylandShortcutTrigger.TryFormat(hotkey, out var trigger));
        Assert.Equal(expected, trigger);
    }

    private sealed class TestRegistration : IActivationHotkeyRegistration
    {
        public void Dispose()
        {
        }
    }
}