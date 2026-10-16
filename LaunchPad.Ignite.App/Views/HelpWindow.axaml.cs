using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LaunchPad.Ignite.App.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
        => Close();
}
