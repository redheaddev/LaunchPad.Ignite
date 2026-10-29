using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace LaunchPad.Ignite.App.Views;

public partial class SecretRevealWindow : Window
{
    private readonly Func<Task> _copySecretAsync;

    public SecretRevealWindow(string title, string value, Func<Task> copySecretAsync)
    {
        InitializeComponent();
        EscapeToClose.Attach(this);
        Title = title;
        _copySecretAsync = copySecretAsync;
        ValueBox.Text = value;
        AddHandler(InputElement.KeyDownEvent, SecretRevealWindow_KeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
        Closed += (_, _) => ValueBox.Clear();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        await _copySecretAsync();
        Close();
    }

    private async void SecretRevealWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C
            && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            await _copySecretAsync();
            Close();
            e.Handled = true;
        }
    }
}
