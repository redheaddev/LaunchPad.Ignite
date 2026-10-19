using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LaunchPad.Ignite.App.Views;

internal static class EscapeToClose
{
    public static void Attach(Window window)
    {
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not Window window)
        {
            return;
        }

        e.Handled = true;
        window.Close();
    }
}