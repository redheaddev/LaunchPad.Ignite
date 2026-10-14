using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LaunchPad.Ignite.App.ViewModels;

namespace LaunchPad.Ignite.App.Views;

public partial class ImportExportWindow : Window
{
    private readonly ImportExportViewModel _viewModel;

    public ImportExportWindow(ImportExportViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e)
        => _viewModel.RefreshInventory();

    private async void BrowseImport_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import command pack",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Command pack manifest") { Patterns = ["*.json"] }]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            _viewModel.ImportPath = path;
        }
    }

    private async void BrowseAddon_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import add-on",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("LaunchPad add-on") { Patterns = ["*.zip"] }]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            _viewModel.AddonPath = path;
        }
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export command pack",
            SuggestedFileName = $"{_viewModel.ExportPackId}.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("Command pack manifest") { Patterns = ["*.json"] }]
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _viewModel.ExportToFile(path);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
        => Close();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CloseRequested -= Close;
        base.OnClosed(e);
    }
}
