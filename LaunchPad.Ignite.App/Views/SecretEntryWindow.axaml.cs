using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LaunchPad.Ignite.App.Views;

public partial class SecretEntryWindow : Window
{
    private readonly bool _requireConfirmation;

    private TextBox EntryBoxControl => this.FindControl<TextBox>(nameof(EntryBox))
        ?? throw new InvalidOperationException("The EntryBox control was not found.");

    private TextBox ConfirmationBoxControl => this.FindControl<TextBox>(nameof(ConfirmationBox))
        ?? throw new InvalidOperationException("The ConfirmationBox control was not found.");

    private CheckBox ShowPasswordBoxControl => this.FindControl<CheckBox>(nameof(ShowPasswordBox))
        ?? throw new InvalidOperationException("The ShowPasswordBox control was not found.");

    private TextBlock ErrorTextControl => this.FindControl<TextBlock>(nameof(ErrorText))
        ?? throw new InvalidOperationException("The ErrorText control was not found.");

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    public SecretEntryWindow(string title, bool requireConfirmation)
    {
        InitializeComponent();
        EscapeToClose.Attach(this);
        Title = title;
        _requireConfirmation = requireConfirmation;
        ConfirmationBoxControl.IsVisible = requireConfirmation;
        ConfirmationBoxControl.IsEnabled = requireConfirmation;
        Opened += (_, _) => EntryBoxControl.Focus();

        Closed += (_, _) =>
        {
            EntryBoxControl.Clear();
            ConfirmationBoxControl.Clear();
        };
    }

    private void ShowPasswordBox_Changed(object? sender, RoutedEventArgs e)
    {
        var passwordChar = ShowPasswordBoxControl.IsChecked == true ? '\0' : '●';
        EntryBoxControl.PasswordChar = passwordChar;
        ConfirmationBoxControl.PasswordChar = passwordChar;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        if (_requireConfirmation && EntryBoxControl.Text != ConfirmationBoxControl.Text)
        {
            ErrorTextControl.Text = "The entries do not match.";
            ErrorTextControl.IsVisible = true;
            ConfirmationBoxControl.Focus();
            return;
        }

        Close(EntryBoxControl.Text ?? string.Empty);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
