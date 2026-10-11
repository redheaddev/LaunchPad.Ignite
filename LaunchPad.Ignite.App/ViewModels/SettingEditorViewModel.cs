using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.ViewModels;

public partial class SettingEditorViewModel : ViewModelBase
{
    private readonly CommandSettingDefinition _definition;
    private readonly ProfileSettingsStore _settingsStore;
    private readonly Action _settingsChanged;

    private string _value;
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public SettingEditorViewModel(
        CommandSettingDefinition definition,
        string value,
        ProfileSettingsStore settingsStore,
        Action settingsChanged)
    {
        _definition = definition;
        _value = value;
        _settingsStore = settingsStore;
        _settingsChanged = settingsChanged;
    }

    public string Label => _definition.Label;
    public string Description => _definition.Description;
    public string Token => $"{{{_definition.QualifiedName}}}";
    public bool IsSecretName => _definition.Type == "secretName";
    public bool IsSelect => _definition.Type == "select";
    public bool IsNotSelect => !IsSelect;
    public bool IsDirectory => _definition.Type == "directory";
    public IReadOnlyList<string> Options => _definition.Options ?? [];

    [RelayCommand]
    private void SaveSetting()
    {
        _settingsStore.SetValue(_definition.QualifiedName, Value);
        _settingsChanged();
    }
}
