using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace LaunchPad.Ignite.App.Views;

public partial class SecretRevealWindow : Window
{
    private readonly Func<Task> _copySecretAsync;

    private TextBox ValueBoxControl => this.FindControl<TextBox>(nameof(ValueBox))
        ?? throw new InvalidOperationException("The ValueBox control was not found.");

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    public SecretRevealWindow(string title, string value, Func<Task> copySecretAsync)
    {
        InitializeComponent();
        EscapeToClose.Attach(this);
        Title = title;
        _copySecretAsync = copySecretAsync;
        ValueBoxControl.Text = value;
        AddHandler(InputElement.KeyDownEvent, SecretRevealWindow_KeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            ValueBoxControl.Focus();
            ValueBoxControl.SelectAll();
        };
        Closed += (_, _) => ValueBoxControl.Clear();
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
