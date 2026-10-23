using System;

namespace LaunchPad.Ignite.App.Services.WindowBehavior.Linux;

internal static class LinuxSessionDetector
{
    public static bool IsWaylandSession(string? sessionType, string? waylandDisplay)
        => string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(sessionType) && !string.IsNullOrWhiteSpace(waylandDisplay));

    public static bool IsX11Session(string? sessionType, string? display)
        => string.Equals(sessionType, "x11", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(sessionType) && !string.IsNullOrWhiteSpace(display));
}