using LaunchPad.Ignite.App.Services.WindowBehavior;
using LaunchPad.Ignite.App.Services.WindowBehavior.Linux;
using LaunchPad.Ignite.App.Services.Hotkeys;

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
        var platform = new LinuxWindowPlatformBehavior(
            "wayland",
            null,
            (_, _) => expectedRegistration);

        var registration = platform.TryRegisterActivationHotkey("Alt+Space", () => { }, out var error);

        Assert.False(platform.SupportsGlobalKeyboardHook);
        Assert.Same(expectedRegistration, registration);
        Assert.Null(error);
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
            "/org/freedesktop/portal/desktop/request/_1_42/request_launchpad_test",
            requestPath);
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