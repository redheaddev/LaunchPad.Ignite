using Avalonia.Controls;
using Avalonia.Interactivity;
using LaunchPad.Ignite.App.ViewModels;

namespace LaunchPad.Ignite.App.Views;

public partial class HelpWindow : Window
{
    public HelpWindow(HelpViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        EscapeToClose.Attach(this);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
        => Close();
}
