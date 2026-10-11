using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LaunchPad.Ignite.App.ViewModels;

namespace LaunchPad.Ignite.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }

    private void ExtensionEnabled_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.SetExtensionEnabled((sender as CheckBox)?.IsChecked == true);
        }
    }
}
