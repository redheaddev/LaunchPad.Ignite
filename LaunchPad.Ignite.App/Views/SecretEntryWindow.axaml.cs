using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LaunchPad.Ignite.App.Views;

public partial class SecretEntryWindow : Window
{
    private readonly bool _requireConfirmation;
    private readonly bool _displayOnly;

    public SecretEntryWindow(string title, bool requireConfirmation, string? displayedValue = null)
    {
        InitializeComponent();
        EscapeToClose.Attach(this);
        Title = title;
        _requireConfirmation = requireConfirmation;
        _displayOnly = displayedValue is not null;
        ConfirmationBox.IsVisible = requireConfirmation;
        ConfirmationBox.IsEnabled = requireConfirmation;
        Opened += (_, _) => EntryBox.Focus();
        if (_displayOnly)
        {
            EntryBox.Text = displayedValue;
            EntryBox.IsReadOnly = true;
            CancelButton.IsVisible = false;
            ConfirmButton.Content = "Close";
        }

        Closed += (_, _) =>
        {
            EntryBox.Clear();
            ConfirmationBox.Clear();
        };
    }

    private void ShowPasswordBox_Changed(object? sender, RoutedEventArgs e)
    {
        var passwordChar = ShowPasswordBox.IsChecked == true ? '\0' : '●';
        EntryBox.PasswordChar = passwordChar;
        ConfirmationBox.PasswordChar = passwordChar;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        if (_requireConfirmation && EntryBox.Text != ConfirmationBox.Text)
        {
            ErrorText.Text = "The entries do not match.";
            ErrorText.IsVisible = true;
            ConfirmationBox.Focus();
            return;
        }

        Close(_displayOnly ? null : EntryBox.Text ?? string.Empty);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
