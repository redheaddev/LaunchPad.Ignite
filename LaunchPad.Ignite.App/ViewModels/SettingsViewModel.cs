using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ProfileSettingsStore _settingsStore;
    private readonly Action _settingsChanged;
    private readonly IReadOnlyList<CommandSettingDefinition> _applicationSettings;
    private readonly ExtensionStateStore _extensionStateStore;

    private SettingsNavigationItem? _selectedNavigationItem;
    public IReadOnlyList<SettingsNavigationItem> NavigationItems { get; }
    public IReadOnlyList<SettingEditorViewModel> Settings { get; private set; } = [];
    public string Header { get; private set; } = "General";
    public string Subheader { get; private set; } = string.Empty;
    public bool IsExtensionEnabled { get; private set; } = true;
    public bool IsExtensionSelected => SelectedNavigationItem?.Category == "extension";

    public SettingsViewModel(
        IReadOnlyList<CommandSettingDefinition> applicationSettings,
        LoadedExtensions extensions,
        ProfileSettingsStore settingsStore,
        ExtensionStateStore extensionStateStore,
        Action settingsChanged)
    {
        _settingsStore = settingsStore;
        _settingsChanged = settingsChanged;
        _applicationSettings = applicationSettings;
        _extensionStateStore = extensionStateStore;
        var extensionSettings = extensions.Schema.Settings
            .GroupBy(setting => setting.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CommandSettingDefinition>)group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var navigation = new List<SettingsNavigationItem>
        {
            new("General", "application", "")
            {
                AvailableSettings = applicationSettings
            },
            new("Commands", "commands", ""),
            new("Secrets", "secrets", ""),
            new("Extensions", "extensions", "")
        };
        foreach (var extension in extensions.Extensions.OrderBy(extension => extension.Id, StringComparer.OrdinalIgnoreCase))
        {
            extensionSettings.TryGetValue(extension.Id, out var settingsForExtension);
            var extensionLabel = settingsForExtension?.FirstOrDefault()?.Namespace ?? extension.Id;
            navigation.Add(new SettingsNavigationItem(extensionLabel, "extension", extension.Id)
            {
                IsEnabled = extension.Enabled,
                AvailableSettings = settingsForExtension ?? []
            });
        }

        NavigationItems = navigation;
        SelectedNavigationItem = navigation.FirstOrDefault(item => item.Category == "application");
    }

    public SettingsNavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (SetProperty(ref _selectedNavigationItem, value) && value is not null)
            {
                UpdateSelection(value);
            }
        }
    }

    private void UpdateSelection(SettingsNavigationItem value)
    {
        Header = value.Name;
        if (value.Category == "extension")
        {
            var settingDefinitions = value.AvailableSettings;
            Subheader = $"{settingDefinitions.Count} setting(s) available. Token prefix: {value.Id}";
            IsExtensionEnabled = _extensionStateStore.IsEnabled(value.Id);
            Settings = settingDefinitions.Select(CreateEditor).ToArray();
        }
        else if (value.Category == "application")
        {
            Subheader = "Application settings";
            Settings = _applicationSettings.Select(CreateEditor).ToArray();
        }
        else
        {
            Subheader = string.Empty;
            Settings = [];
        }

        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(Subheader));
        OnPropertyChanged(nameof(IsExtensionEnabled));
        OnPropertyChanged(nameof(IsExtensionSelected));
        OnPropertyChanged(nameof(Settings));
    }

    private SettingEditorViewModel CreateEditor(CommandSettingDefinition definition)
    {
        var value = _settingsStore.GetValue(definition.QualifiedName, definition.DefaultValue);
        return new SettingEditorViewModel(definition, value, _settingsStore, _settingsChanged);
    }

    [RelayCommand]
    private void ToggleExtension()
    {
        if (SelectedNavigationItem?.Category != "extension")
        {
            return;
        }

        _extensionStateStore.SetEnabled(SelectedNavigationItem.Id, IsExtensionEnabled);
        _settingsChanged();
    }

    public void SetExtensionEnabled(bool enabled)
    {
        if (_selectedNavigationItem?.Category == "extension")
        {
            _extensionStateStore.SetEnabled(_selectedNavigationItem.Id, enabled);
            _settingsChanged();
        }

        IsExtensionEnabled = enabled;
        OnPropertyChanged(nameof(IsExtensionEnabled));
    }

    public sealed class SettingsNavigationItem(string name, string category, string id)
    {
        public string Name { get; } = name;
        public string Category { get; } = category;
        public string Id { get; } = id;
        public bool IsEnabled { get; init; }
        public IReadOnlyList<CommandSettingDefinition> AvailableSettings { get; init; } = [];
        public CommandSettingDefinition? SettingDefinition { get; init; }
    }
}
