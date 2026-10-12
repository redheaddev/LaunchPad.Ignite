using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        Closed += (_, _) => ColorThemeService.ThemeChanged -= RefreshThemeResources;
        ColorThemeService.ThemeChanged += RefreshThemeResources;
    }

    private void OpenThemeReference_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel
            && Uri.TryCreate(viewModel.SelectedColorThemeUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void SaveThemeReference_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.SaveThemeReferenceUrl();
        }
    }

    private void CreateTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.CreateTheme(viewModel.NewThemeName);
        }
    }

    private void RenameTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.RenameSelectedColorTheme(viewModel.RenameThemeName);
        }
    }

    private void DeleteTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.DeleteSelectedColorTheme();
        }
    }

    private async void ImportTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            await viewModel.ImportThemeColorsAsync(viewModel.ThemeImportUrl);
        }
    }

    private void ThemeColorHex_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is TextBox textBox && textBox.Tag is string property)
        {
            viewModel.SetThemeColor(property, textBox.Text);
        }
    }

    private void SaveThemeColors_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.SaveThemeColorChanges();
        }
    }

    private void ExtensionEnabled_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.SetExtensionEnabled((sender as CheckBox)?.IsChecked == true);
        }
    }

    private void ColorTheme_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ColorTheme theme)
            {
                viewModel.SelectColorTheme(theme.Id);
                viewModel.RefreshSelectedColorTheme();
            }
        }
    }

    private void RefreshThemeResources()
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.RefreshSelectedColorTheme();
        }
    }
}
